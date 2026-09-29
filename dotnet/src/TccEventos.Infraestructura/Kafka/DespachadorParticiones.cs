using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Un trabajador por partición: dentro de una partición los mensajes se procesan EN ORDEN (el orden por guía
/// se conserva porque la guía es la clave), y particiones distintas avanzan EN PARALELO.
/// </summary>
/// <remarks>
/// Todos los métodos públicos se llaman desde el hilo del consumidor de Kafka (bucle y manejadores de
/// rebalanceo), así que el estado interno no necesita candados; los trabajadores solo leen su propia cola.
/// </remarks>
/// <param name="consumidor">Consumidor de Kafka (para pausar, reanudar y rebobinar particiones).</param>
/// <param name="procesar">Función que maneja y marca cada mensaje.</param>
/// <param name="capacidadPorParticion">Mensajes en cola por partición antes de pausarla.</param>
/// <param name="esperaMaximaAlRevocar">Cuánto esperar el mensaje en curso al revocar una partición.</param>
/// <param name="logger">Registro de fallas inesperadas.</param>
/// <param name="detener">Se activa cuando el host se detiene.</param>
internal sealed class DespachadorParticiones(
    IConsumer<string, string> consumidor,
    Func<ConsumeResult<string, string>, CancellationToken, Task> procesar,
    int capacidadPorParticion,
    TimeSpan esperaMaximaAlRevocar,
    ILogger logger,
    CancellationToken detener)
{
    /// <summary>El trabajador de una partición: su cola, su tarea y su cancelación.</summary>
    /// <param name="cola">Cola acotada de mensajes pendientes de la partición.</param>
    /// <param name="cancelacion">Cancelación propia, ligada al apagado del host.</param>
    private sealed class Trabajador(Channel<ConsumeResult<string, string>> cola, CancellationTokenSource cancelacion)
    {
        /// <summary>Mensajes pendientes de la partición, en orden.</summary>
        public Channel<ConsumeResult<string, string>> Cola { get; } = cola;

        /// <summary>Permite detener al trabajador al revocar la partición o apagar el servicio.</summary>
        public CancellationTokenSource Cancelacion { get; } = cancelacion;

        /// <summary>Tarea que procesa la cola.</summary>
        public Task Tarea { get; set; } = Task.CompletedTask;

        /// <summary>Si es verdadero, el trabajador deja de procesar lo que queda en cola (partición revocada).</summary>
        public volatile bool Descartar;
    }

    /// <summary>Trabajador activo de cada partición asignada.</summary>
    private readonly Dictionary<TopicPartition, Trabajador> _trabajadores = [];

    /// <summary>Particiones pausadas por contrapresión.</summary>
    private readonly HashSet<TopicPartition> _pausadas = [];

    /// <summary>Primera falla inesperada de un trabajador, para relanzarla en el hilo del consumidor.</summary>
    private volatile ExceptionDispatchInfo? _falla;

    /// <summary>
    /// Entrega el mensaje al trabajador de su partición; si la cola está llena, pausa la partición
    /// y rebobina para volver a leer el mensaje después (contrapresión).
    /// </summary>
    /// <param name="registro">Mensaje leído de Kafka.</param>
    public void Despachar(ConsumeResult<string, string> registro)
    {
        if (!_trabajadores.TryGetValue(registro.TopicPartition, out var trabajador))
            _trabajadores[registro.TopicPartition] = trabajador = Crear(registro.TopicPartition);

        if (trabajador.Cola.Writer.TryWrite(registro))
            return;

        // Cola llena (contrapresión): se pausa la partición y se rebobina para volver a leer este mensaje luego.
        consumidor.Pause([registro.TopicPartition]);
        consumidor.Seek(registro.TopicPartitionOffset);
        _pausadas.Add(registro.TopicPartition);
    }

    /// <summary>Reanuda las particiones pausadas cuya cola bajó a la mitad.</summary>
    public void ReanudarDescongestionadas()
    {
        if (_pausadas.Count == 0)
            return;

        var listas = _pausadas
            .Where(tp => !_trabajadores.TryGetValue(tp, out var t) || t.Cola.Reader.Count <= capacidadPorParticion / 2)
            .ToList();
        if (listas.Count == 0)
            return;

        consumidor.Resume(listas);
        _pausadas.ExceptWith(listas);
    }

    /// <summary>
    /// Relanza en el hilo del consumidor la falla inesperada de un trabajador, si la hubo.
    /// </summary>
    /// <remarks>
    /// Un trabajador que falla de forma inesperada no puede dejar su partición detenida en silencio:
    /// la falla se relanza en el hilo del consumidor y el servicio se detiene (igual que en modo secuencial).
    /// </remarks>
    public void LanzarSiFallo() => _falla?.Throw();

    /// <summary>
    /// Detiene los trabajadores de las particiones revocadas o perdidas en un rebalanceo.
    /// </summary>
    /// <remarks>
    /// El mensaje en curso termina (hasta el límite de espera) y lo que quedaba en cola se descarta.
    /// El nuevo dueño lo relee desde el último offset guardado.
    /// </remarks>
    /// <param name="particiones">Particiones que dejan de pertenecer a esta instancia.</param>
    public void Revocar(IEnumerable<TopicPartition> particiones)
    {
        var revocados = new List<Trabajador>();
        foreach (var particion in particiones)
        {
            _pausadas.Remove(particion);
            if (!_trabajadores.Remove(particion, out var trabajador))
                continue;

            trabajador.Descartar = true;
            trabajador.Cola.Writer.TryComplete();
            trabajador.Cancelacion.CancelAfter(esperaMaximaAlRevocar);
            revocados.Add(trabajador);
        }

        Task.WhenAll(revocados.Select(t => t.Tarea)).Wait(esperaMaximaAlRevocar + TimeSpan.FromSeconds(5));
        foreach (var trabajador in revocados)
            trabajador.Cancelacion.Dispose();
    }

    /// <summary>Detiene todos los trabajadores al apagar el servicio.</summary>
    /// <returns>Una tarea ya completada cuando todos terminaron.</returns>
    public Task DetenerAsync()
    {
        Revocar(_trabajadores.Keys.ToList());
        return Task.CompletedTask;
    }

    /// <summary>Crea el trabajador de una partición, con su cola acotada, y lo pone a trabajar.</summary>
    /// <param name="particion">Partición que atenderá.</param>
    /// <returns>El trabajador en ejecución.</returns>
    private Trabajador Crear(TopicPartition particion)
    {
        var cola = Channel.CreateBounded<ConsumeResult<string, string>>(new BoundedChannelOptions(capacidadPorParticion)
        {
            SingleReader = true,
            SingleWriter = true
        });
        var trabajador = new Trabajador(cola, CancellationTokenSource.CreateLinkedTokenSource(detener));
        trabajador.Tarea = Task.Run(() => TrabajarAsync(trabajador, particion));
        return trabajador;
    }

    /// <summary>
    /// Bucle del trabajador: procesa en orden los mensajes de su partición hasta que se revoca o se apaga el servicio.
    /// </summary>
    /// <param name="trabajador">Trabajador que ejecuta el bucle.</param>
    /// <param name="particion">Partición que atiende.</param>
    /// <returns>Una tarea que termina al revocar, apagar o fallar.</returns>
    private async Task TrabajarAsync(Trabajador trabajador, TopicPartition particion)
    {
        var ct = trabajador.Cancelacion.Token;
        try
        {
            while (await trabajador.Cola.Reader.WaitToReadAsync(ct))
            {
                while (!trabajador.Descartar && trabajador.Cola.Reader.TryRead(out var registro))
                    await procesar(registro, ct);

                if (trabajador.Descartar)
                    return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Revocación o apagado: el mensaje en curso no se marcó y se volverá a leer.
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "El trabajador de la partición {Particion} falló de forma inesperada", particion.Partition.Value);
            _falla = ExceptionDispatchInfo.Capture(ex);
        }
    }
}

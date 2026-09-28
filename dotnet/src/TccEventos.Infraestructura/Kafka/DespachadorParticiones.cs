using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace TccEventos.Infraestructura.Kafka;

/// Un trabajador por partición: dentro de una partición los mensajes se procesan EN ORDEN (el orden por guía
/// se conserva porque la guía es la clave), y particiones distintas avanzan EN PARALELO.
/// Todos los métodos públicos se llaman desde el hilo del consumidor de Kafka (bucle y manejadores de
/// rebalanceo), así que el estado interno no necesita candados; los trabajadores solo leen su propia cola.
internal sealed class DespachadorParticiones(
    IConsumer<string, string> consumidor,
    Func<ConsumeResult<string, string>, CancellationToken, Task> procesar,
    int capacidadPorParticion,
    TimeSpan esperaMaximaAlRevocar,
    ILogger logger,
    CancellationToken detener)
{
    private sealed class Trabajador(Channel<ConsumeResult<string, string>> cola, CancellationTokenSource cancelacion)
    {
        public Channel<ConsumeResult<string, string>> Cola { get; } = cola;
        public CancellationTokenSource Cancelacion { get; } = cancelacion;
        public Task Tarea { get; set; } = Task.CompletedTask;
        public volatile bool Descartar;
    }

    private readonly Dictionary<TopicPartition, Trabajador> _trabajadores = [];
    private readonly HashSet<TopicPartition> _pausadas = [];
    private volatile ExceptionDispatchInfo? _falla;

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

    /// Reanuda las particiones pausadas cuya cola bajó a la mitad.
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

    /// Un trabajador que falla de forma inesperada no puede dejar su partición detenida en silencio:
    /// la falla se relanza en el hilo del consumidor y el servicio se detiene (igual que en modo secuencial).
    public void LanzarSiFallo() => _falla?.Throw();

    /// Partición revocada o perdida en un rebalanceo: el mensaje en curso termina (hasta el límite de espera)
    /// y lo que quedaba en cola se descarta. El nuevo dueño lo relee desde el último offset guardado.
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

    public Task DetenerAsync()
    {
        Revocar(_trabajadores.Keys.ToList());
        return Task.CompletedTask;
    }

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

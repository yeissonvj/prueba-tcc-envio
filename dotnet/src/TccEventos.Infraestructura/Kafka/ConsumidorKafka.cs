using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Qué consumir de Kafka y cómo.
/// </summary>
/// <param name="Topico">Tópico a consumir.</param>
/// <param name="Grupo">Grupo de consumo; cada grupo lee todos los mensajes a su propio ritmo.</param>
/// <param name="Vencimiento">
/// Cuándo se puede procesar cada mensaje (tópicos de reintento con demora). Solo compatible con el consumo secuencial.
/// </param>
/// <param name="ParticionesEnParalelo">
/// Un trabajador por partición: orden dentro de la partición y particiones en paralelo.
/// </param>
/// <param name="CapacidadPorParticion">Mensajes en cola por partición antes de pausarla (contrapresión).</param>
public record SuscripcionKafka(
    string Topico,
    string Grupo,
    Func<MensajeKafka, DateTimeOffset?>? Vencimiento = null,
    bool ParticionesEnParalelo = false,
    int CapacidadPorParticion = 200);

/// <summary>
/// Bucle de consumo reutilizable (patrón Plantilla): leer → (esperar vencimiento) → manejar → marcar offset.
/// Cada host aporta solo la función que maneja el mensaje.
/// </summary>
/// <remarks>
/// Offsets: se guardan (StoreOffset) solo DESPUÉS de manejar el mensaje y Kafka los confirma en lote cada
/// segundo. Si el proceso cae se releen unos pocos mensajes; los manejadores son idempotentes.
/// El manejador solo debe retornar cuando el mensaje quedó resuelto (procesado, reprogramado o en DLQ).
/// </remarks>
/// <param name="kafka">Opciones de conexión a Kafka.</param>
/// <param name="suscripcion">Qué tópico y grupo consumir, y de qué forma.</param>
/// <param name="manejar">Función que resuelve cada mensaje.</param>
/// <param name="registros">Fábrica de loggers (el logger lleva el nombre del tópico).</param>
/// <param name="reloj">Reloj para calcular los vencimientos (reemplazable en pruebas).</param>
public sealed class ConsumidorKafka(
    OpcionesKafka kafka,
    SuscripcionKafka suscripcion,
    Func<MensajeKafka, CancellationToken, Task> manejar,
    ILoggerFactory registros,
    TimeProvider reloj) : BackgroundService
{
    /// <summary>Logger con el tópico en su nombre, para distinguir los consumidores de un mismo host.</summary>
    private readonly ILogger _logger = registros.CreateLogger($"{typeof(ConsumidorKafka).FullName}[{suscripcion.Topico}]");

    // Tiempo máximo que un rebalanceo espera a que termine el mensaje en curso de una partición revocada.
    /// <summary>Espera máxima por el mensaje en curso cuando se revoca una partición.</summary>
    private static readonly TimeSpan EsperaMaximaAlRevocar = TimeSpan.FromSeconds(30);

    // Consume() es bloqueante: corre en su propio hilo para no detener el arranque del host.
    /// <summary>Punto de entrada del servicio en segundo plano: arranca el bucle de consumo en un hilo propio.</summary>
    /// <param name="ct">Se activa cuando el host se detiene.</param>
    /// <returns>Una tarea que termina cuando el consumo se detiene.</returns>
    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.Factory.StartNew(() => ConsumirAsync(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    /// <summary>
    /// Bucle principal: configura el consumidor durable, se suscribe y entrega cada mensaje al manejador
    /// (en orden o por partición en paralelo) hasta que el host se detiene.
    /// </summary>
    /// <param name="ct">Se activa cuando el host se detiene.</param>
    /// <returns>Una tarea que termina al cerrar el consumidor de forma ordenada.</returns>
    /// <exception cref="InvalidOperationException">Si se pide vencimiento junto con particiones en paralelo.</exception>
    private async Task ConsumirAsync(CancellationToken ct)
    {
        if (suscripcion.ParticionesEnParalelo && suscripcion.Vencimiento is not null)
            throw new InvalidOperationException("La espera por vencimiento solo es compatible con el consumo secuencial.");

        var configuracion = kafka.ConfigurarConexion(new ConsumerConfig
        {
            GroupId = suscripcion.Grupo,
            EnableAutoCommit = true,            // confirma en lote...
            EnableAutoOffsetStore = false,      // ...solo los offsets que marcamos tras manejar
            AutoCommitIntervalMs = 1000,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            MaxPollIntervalMs = 600_000         // tolera reintentos bloqueantes largos antes de ceder la partición
        });

        DespachadorParticiones? despachador = null;
        using var consumidor = new ConsumerBuilder<string, string>(configuracion)
            .SetPartitionsAssignedHandler((_, p) => _logger.LogInformation("Particiones asignadas: {Particiones}", Listar(p.Select(x => x.Partition))))
            .SetPartitionsRevokedHandler((_, p) =>
            {
                _logger.LogInformation("Particiones revocadas: {Particiones}", Listar(p.Select(x => x.Partition)));
                despachador?.Revocar(p.Select(x => x.TopicPartition));
            })
            .SetPartitionsLostHandler((_, p) =>
            {
                _logger.LogWarning("Particiones perdidas: {Particiones}", Listar(p.Select(x => x.Partition)));
                despachador?.Revocar(p.Select(x => x.TopicPartition));
            })
            .SetErrorHandler((_, error) => _logger.LogWarning("Kafka: {Razon}", error.Reason))
            .Build();

        if (suscripcion.ParticionesEnParalelo)
            despachador = new DespachadorParticiones(consumidor, (r, token) => ManejarYMarcarAsync(consumidor, r, token),
                suscripcion.CapacidadPorParticion, EsperaMaximaAlRevocar, _logger, ct);

        consumidor.Subscribe(suscripcion.Topico);
        _logger.LogInformation("Consumiendo {Topico} como grupo {Grupo} ({Modo})", suscripcion.Topico, suscripcion.Grupo,
            despachador is null ? "secuencial" : "particiones en paralelo");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? registro;
                try
                {
                    // En paralelo se usa un tiempo de espera corto: con particiones pausadas por contrapresión,
                    // Consume podría no volver nunca y no se reanudarían.
                    registro = despachador is null ? consumidor.Consume(ct) : consumidor.Consume(TimeSpan.FromMilliseconds(100));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Error al leer de Kafka");
                    continue;
                }

                if (despachador is not null)
                {
                    despachador.LanzarSiFallo();
                    if (registro?.Message is not null)
                        despachador.Despachar(registro);
                    despachador.ReanudarDescongestionadas();
                    continue;
                }

                if (registro?.Message is null)
                    continue;

                if (suscripcion.Vencimiento?.Invoke(AMensaje(registro)) is { } vence)
                    EsperarVencimiento(consumidor, registro, vence, ct);

                await ManejarYMarcarAsync(consumidor, registro, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (despachador is not null)
                await despachador.DetenerAsync();
            consumidor.Close(); // confirma los offsets guardados y sale del grupo de forma ordenada
        }
    }

    /// <summary>Maneja el mensaje y, solo después, guarda su offset para confirmarlo.</summary>
    /// <param name="consumidor">Consumidor de Kafka dueño de la partición.</param>
    /// <param name="registro">Mensaje leído.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el mensaje quedó resuelto y marcado.</returns>
    private async Task ManejarYMarcarAsync(IConsumer<string, string> consumidor, ConsumeResult<string, string> registro, CancellationToken ct)
    {
        await ManejarConTrazaAsync(AMensaje(registro), ct);
        try
        {
            consumidor.StoreOffset(registro);
        }
        catch (KafkaException ex)
        {
            // La partición se reasignó mientras se manejaba: su nuevo dueño lo repetirá (idempotente).
            _logger.LogWarning(ex, "La partición {Particion} ya no pertenece a esta instancia", registro.Partition.Value);
        }
    }

    /// <summary>Espera hasta que el mensaje de reintento se pueda procesar, sin salir del grupo de consumo.</summary>
    /// <remarks>
    /// Los tópicos de reintento tienen demora fija, así que el primer mensaje es el que vence antes.
    /// Se pausan las particiones y se sigue haciendo poll: esperar una hora sin poll sacaría al consumidor del grupo.
    /// </remarks>
    /// <param name="consumidor">Consumidor de Kafka.</param>
    /// <param name="actual">Mensaje que se está esperando.</param>
    /// <param name="vence">Momento a partir del cual se puede procesar.</param>
    /// <param name="ct">Token de cancelación.</param>
    private void EsperarVencimiento(IConsumer<string, string> consumidor, ConsumeResult<string, string> actual, DateTimeOffset vence, CancellationToken ct)
    {
        if (vence <= reloj.GetUtcNow())
            return;

        _logger.LogDebug("Esperando hasta {Vence} para reintentar {Topico}[{Particion}]@{Offset}",
            vence, actual.Topic, actual.Partition.Value, actual.Offset.Value);
        consumidor.Pause(consumidor.Assignment);
        try
        {
            while (reloj.GetUtcNow() < vence)
            {
                ct.ThrowIfCancellationRequested();
                var restante = vence - reloj.GetUtcNow();
                var intruso = consumidor.Consume(restante < TimeSpan.FromSeconds(1) ? restante : TimeSpan.FromSeconds(1));

                // Una partición recién asignada no está pausada: se devuelve su mensaje para leerlo después.
                if (intruso?.Message is not null)
                {
                    consumidor.Seek(intruso.TopicPartitionOffset);
                    consumidor.Pause([intruso.TopicPartition]);
                }
            }
        }
        finally
        {
            consumidor.Resume(consumidor.Assignment);
        }
    }

    /// <summary>
    /// Ejecuta el manejador dentro de una traza que continúa la de quien publicó (traceparent del encabezado).
    /// </summary>
    /// <param name="mensaje">Mensaje a manejar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el manejador terminó; si falla, la traza queda marcada con error.</returns>
    private async Task ManejarConTrazaAsync(MensajeKafka mensaje, CancellationToken ct)
    {
        ActivityContext.TryParse(mensaje.Encabezado(ProductorKafka.EncabezadoTraza), null, out var padre);
        using var actividad = Telemetria.Trazas.StartActivity($"{mensaje.Topico} process", ActivityKind.Consumer, padre);
        actividad?.SetTag("messaging.system", "kafka")
            .SetTag("messaging.destination.name", mensaje.Topico)
            .SetTag("messaging.consumer.group.name", suscripcion.Grupo)
            .SetTag("messaging.kafka.destination.partition", mensaje.Particion)
            .SetTag("messaging.kafka.offset", mensaje.Offset);
        try
        {
            await manejar(mensaje, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            actividad?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>Convierte el mensaje de Confluent.Kafka en un <see cref="MensajeKafka"/> independiente de la librería.</summary>
    /// <param name="registro">Mensaje leído de Kafka.</param>
    /// <returns>El mensaje con tópico, partición, offset, clave, valor, encabezados y marca de tiempo.</returns>
    private static MensajeKafka AMensaje(ConsumeResult<string, string> registro) => new(
        registro.Topic,
        registro.Partition.Value,
        registro.Offset.Value,
        registro.Message.Key,
        registro.Message.Value,
        registro.Message.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes())),
        registro.Message.Timestamp.Type == TimestampType.NotAvailable
            ? null
            : new DateTimeOffset(registro.Message.Timestamp.UtcDateTime, TimeSpan.Zero));

    /// <summary>Lista los números de partición separados por coma, para los logs.</summary>
    /// <param name="particiones">Particiones a listar.</param>
    /// <returns>Por ejemplo <c>0,3,7</c>.</returns>
    private static string Listar(IEnumerable<Partition> particiones) => string.Join(",", particiones.Select(p => p.Value));
}

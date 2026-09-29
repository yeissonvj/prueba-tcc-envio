using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Único lugar con la configuración de durabilidad del productor. Todo lo que publica en Kafka
/// (ingesta, outbox, DLQ) pasa por aquí, así nadie publica con garantías distintas por accidente.
/// </summary>
/// <remarks>Es thread-safe y costoso: se registra como singleton.</remarks>
public sealed class ProductorKafka : IDisposable
{
    /// <summary>Productor de Confluent.Kafka configurado para durabilidad.</summary>
    private readonly IProducer<string, string> _productor;

    /// <summary>Crea el productor con acks=all, idempotencia y compresión lz4.</summary>
    /// <param name="opciones">Opciones de conexión y tiempos de entrega.</param>
    public ProductorKafka(OpcionesKafka opciones)
    {
        var configuracion = opciones.ConfigurarConexion(new ProducerConfig
        {
            Acks = Acks.All,                                   // todas las réplicas en sincronía (≥ 2)
            EnableIdempotence = true,                          // reintentos sin duplicar ni desordenar
            MaxInFlight = 5,                                   // máximo compatible con idempotencia
            MessageTimeoutMs = opciones.TiempoMaximoEntregaMs, // delivery.timeout.ms
            LingerMs = 5,                                      // lotes de hasta 5 ms
            CompressionType = CompressionType.Lz4
        });

        _productor = new ProducerBuilder<string, string>(configuracion).Build();
    }

    /// <summary>Publica un mensaje y espera la confirmación de Kafka con acks=all.</summary>
    /// <param name="topico">Tópico destino.</param>
    /// <param name="clave">Clave del mensaje (el número de guía: define la partición y el orden).</param>
    /// <param name="valor">Contenido del mensaje (JSON).</param>
    /// <param name="encabezados">Encabezados; si trae traceparent, la publicación cuelga de esa traza.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que solo termina con éxito si Kafka confirmó el mensaje.</returns>
    /// <exception cref="PublicacionFallidaException">Kafka no confirmó el mensaje.</exception>
    public async Task PublicarAsync(
        string topico, string clave, string valor,
        IReadOnlyDictionary<string, string> encabezados, CancellationToken ct)
    {
        // Si el llamador trae un contexto de traza (p. ej. el relay del outbox, que publica después y desde
        // otro hilo), el envío cuelga de él; si no, de la actividad actual. Así la traza no se corta.
        var padre = encabezados.TryGetValue(EncabezadoTraza, out var trazaRecibida)
                    && ActivityContext.TryParse(trazaRecibida, null, out var contexto) ? contexto : default;
        using var actividad = padre == default
            ? Telemetria.Trazas.StartActivity($"{topico} publish", ActivityKind.Producer)
            : Telemetria.Trazas.StartActivity($"{topico} publish", ActivityKind.Producer, padre);
        actividad?.SetTag("messaging.system", "kafka").SetTag("messaging.destination.name", topico).SetTag("messaging.operation.type", "send");

        var mensaje = new Message<string, string> { Key = clave, Value = valor, Headers = [] };
        foreach (var (nombre, contenido) in encabezados.Where(e => e.Key != EncabezadoTraza))
            mensaje.Headers.Add(nombre, Encoding.UTF8.GetBytes(contenido));
        if ((actividad?.Id ?? trazaRecibida ?? Activity.Current?.Id) is { } traza)
            mensaje.Headers.Add(EncabezadoTraza, Encoding.UTF8.GetBytes(traza));

        try
        {
            var resultado = await _productor.ProduceAsync(topico, mensaje, ct);

            if (resultado.Status != PersistenceStatus.Persisted)
                throw new PublicacionFallidaException(
                    $"Kafka no confirmó el mensaje de {topico} con clave {clave} (estado {resultado.Status}).");
        }
        catch (KafkaException ex)
        {
            actividad?.SetStatus(ActivityStatusCode.Error, ex.Error.Reason);
            throw new PublicacionFallidaException(
                $"No se pudo publicar en {topico} con clave {clave}: {ex.Error.Reason}", ex);
        }
    }

    /// <summary>Contexto W3C (traceparent) que viaja en los encabezados de Kafka.</summary>
    public const string EncabezadoTraza = "traceparent";

    /// <summary>Envía lo que quedó pendiente (hasta 10 s) y libera el productor.</summary>
    public void Dispose()
    {
        _productor.Flush(TimeSpan.FromSeconds(10));
        _productor.Dispose();
    }
}

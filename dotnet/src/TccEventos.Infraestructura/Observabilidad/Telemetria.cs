using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TccEventos.Infraestructura.Observabilidad;

/// Fuente de trazas e instrumentos de métricas propios. Solo usa System.Diagnostics (parte de .NET):
/// OpenTelemetry los recoge y exporta; el código de negocio no depende del exportador.
public static class Telemetria
{
    public const string Nombre = "TccEventos";

    public static readonly ActivitySource Trazas = new(Nombre);
    private static readonly Meter Metricas = new(Nombre);

    // Latencias en segundos: los cubos por defecto están pensados para milisegundos.
    private static readonly InstrumentAdvice<double> CubosSegundos = new()
    {
        HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300]
    };

    /// resultado: aceptado | duplicado | rechazado | no_durable
    public static readonly Counter<long> EventosRecibidos =
        Metricas.CreateCounter<long>("tcc.eventos.recibidos", "{evento}", "Eventos recibidos por la API de ingesta.");

    public static readonly Counter<long> EventosEnContingencia =
        Metricas.CreateCounter<long>("tcc.eventos.contingencia", "{evento}", "Eventos guardados en contingencia porque Kafka no confirmó.");

    /// resultado: aplicado | tardio | transicion_invalida | duplicado
    public static readonly Counter<long> EventosProcesados =
        Metricas.CreateCounter<long>("tcc.eventos.procesados", "{evento}", "Eventos procesados por el procesador de estado.");

    /// SLO "estado visible p95 < 5 s": desde que Kafka recibió el evento hasta el commit del nuevo estado.
    public static readonly Histogram<double> LatenciaEstado =
        Metricas.CreateHistogram("tcc.latencia.estado", "s", "Latencia desde la ingesta hasta el estado visible.", advice: CubosSegundos);

    /// canal: sms | correo; resultado: enviada | ya_enviada | obsoleta | no_aplica | sin_destino | reintento | fallida
    public static readonly Counter<long> Notificaciones =
        Metricas.CreateCounter<long>("tcc.notificaciones", "{notificacion}", "Intentos de notificación por canal y resultado.");

    /// SLO "notificación p95 < 60 s": desde que el cambio llegó a guias.estados.cambiados hasta el envío.
    public static readonly Histogram<double> LatenciaNotificacion =
        Metricas.CreateHistogram("tcc.latencia.notificacion", "s", "Latencia desde el cambio de estado hasta la notificación.", advice: CubosSegundos);

    /// origen: procesador | notificador
    public static readonly Counter<long> MensajesDlq =
        Metricas.CreateCounter<long>("tcc.dlq", "{mensaje}", "Mensajes enviados a una DLQ.");

    public static KeyValuePair<string, object?> Etiqueta(string nombre, object? valor) => new(nombre, valor);

    /// Nombre de etiqueta en minúsculas y snake_case a partir de un enum (TransicionInvalida → transicion_invalida).
    public static string Texto<T>(T valor) where T : struct, Enum =>
        string.Concat(valor.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));
}

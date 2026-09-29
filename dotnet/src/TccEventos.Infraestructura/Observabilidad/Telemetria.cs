using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TccEventos.Infraestructura.Observabilidad;

/// <summary>
/// Fuente de trazas e instrumentos de métricas propios.
/// </summary>
/// <remarks>
/// Solo usa System.Diagnostics (parte de .NET): OpenTelemetry los recoge y exporta;
/// el código de negocio no depende del exportador.
/// </remarks>
public static class Telemetria
{
    /// <summary>Nombre de la fuente de trazas y del medidor de métricas.</summary>
    public const string Nombre = "TccEventos";

    /// <summary>Fuente de las trazas propias (publicar y procesar mensajes de Kafka).</summary>
    public static readonly ActivitySource Trazas = new(Nombre);

    /// <summary>Medidor donde se crean las métricas propias.</summary>
    private static readonly Meter Metricas = new(Nombre);

    // Latencias en segundos: los cubos por defecto están pensados para milisegundos.
    /// <summary>Límites de los cubos de los histogramas de latencia, en segundos.</summary>
    private static readonly InstrumentAdvice<double> CubosSegundos = new()
    {
        HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300]
    };

    /// <summary>Eventos recibidos por la API. Etiqueta resultado: aceptado | duplicado | rechazado | no_durable.</summary>
    public static readonly Counter<long> EventosRecibidos =
        Metricas.CreateCounter<long>("tcc.eventos.recibidos", "{evento}", "Eventos recibidos por la API de ingesta.");

    /// <summary>Eventos guardados en contingencia porque Kafka no confirmó.</summary>
    public static readonly Counter<long> EventosEnContingencia =
        Metricas.CreateCounter<long>("tcc.eventos.contingencia", "{evento}", "Eventos guardados en contingencia porque Kafka no confirmó.");

    /// <summary>Eventos procesados. Etiqueta resultado: aplicado | tardio | transicion_invalida | duplicado.</summary>
    public static readonly Counter<long> EventosProcesados =
        Metricas.CreateCounter<long>("tcc.eventos.procesados", "{evento}", "Eventos procesados por el procesador de estado.");

    /// <summary>
    /// SLO "estado visible p95 &lt; 5 s": desde que Kafka recibió el evento hasta el commit del nuevo estado.
    /// </summary>
    public static readonly Histogram<double> LatenciaEstado =
        Metricas.CreateHistogram("tcc.latencia.estado", "s", "Latencia desde la ingesta hasta el estado visible.", advice: CubosSegundos);

    /// <summary>
    /// Intentos de notificación. Etiquetas canal: sms | correo; resultado: enviada | ya_enviada | obsoleta |
    /// no_aplica | sin_destino | reintento | fallida.
    /// </summary>
    public static readonly Counter<long> Notificaciones =
        Metricas.CreateCounter<long>("tcc.notificaciones", "{notificacion}", "Intentos de notificación por canal y resultado.");

    /// <summary>
    /// SLO "notificación p95 &lt; 60 s": desde que el cambio llegó a guias.estados.cambiados hasta el envío.
    /// </summary>
    public static readonly Histogram<double> LatenciaNotificacion =
        Metricas.CreateHistogram("tcc.latencia.notificacion", "s", "Latencia desde el cambio de estado hasta la notificación.", advice: CubosSegundos);

    /// <summary>Mensajes enviados a una DLQ. Etiqueta origen: procesador | notificador.</summary>
    public static readonly Counter<long> MensajesDlq =
        Metricas.CreateCounter<long>("tcc.dlq", "{mensaje}", "Mensajes enviados a una DLQ.");

    /// <summary>Crea una etiqueta (nombre y valor) para una métrica.</summary>
    /// <param name="nombre">Nombre de la etiqueta.</param>
    /// <param name="valor">Valor de la etiqueta.</param>
    /// <returns>El par listo para pasar al instrumento.</returns>
    public static KeyValuePair<string, object?> Etiqueta(string nombre, object? valor) => new(nombre, valor);

    /// <summary>
    /// Nombre de etiqueta en minúsculas y snake_case a partir de un enum (TransicionInvalida → transicion_invalida).
    /// </summary>
    /// <typeparam name="T">Tipo del enum.</typeparam>
    /// <param name="valor">Valor del enum.</param>
    /// <returns>El nombre en snake_case.</returns>
    public static string Texto<T>(T valor) where T : struct, Enum =>
        string.Concat(valor.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));
}

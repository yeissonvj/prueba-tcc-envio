using System.Globalization;
using System.Text.Json;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Notificador.Enrutamiento;

/// <summary>
/// Implementación de <see cref="IEnrutadorNotificaciones"/> con Kafka: publica en el tópico de la etapa
/// de reintento (con su hora de vencimiento) o en notificaciones.dlq.
/// </summary>
/// <param name="productor">Productor durable de Kafka.</param>
/// <param name="opciones">Escalera de reintentos y tópico de la DLQ.</param>
/// <param name="reloj">Reloj para calcular el vencimiento.</param>
public sealed class EnrutadorNotificacionesKafka(ProductorKafka productor, OpcionesNotificador opciones, TimeProvider reloj)
    : IEnrutadorNotificaciones
{
    /// <summary>Encabezado con el momento (ISO-8601) a partir del cual se puede reintentar.</summary>
    public const string EncabezadoReintentarDespues = "reintentar-despues";

    /// <summary>Reglas de serialización web (camelCase).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Publica la notificación en el tópico de la etapa que corresponde a su número de intento.</summary>
    /// <param name="pendiente">Notificación pendiente (Intento 1 = primera etapa).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando Kafka confirmó.</returns>
    public Task ProgramarReintentoAsync(NotificacionPendienteV1 pendiente, CancellationToken ct)
    {
        var etapa = opciones.Reintentos[pendiente.Intento - 1];
        var vence = reloj.GetUtcNow() + etapa.Espera;

        return productor.PublicarAsync(
            etapa.Topico,
            pendiente.Cambio.NumeroGuia,
            JsonSerializer.Serialize(pendiente, Json),
            new Dictionary<string, string>
            {
                [EncabezadoReintentarDespues] = vence.ToString("O", CultureInfo.InvariantCulture),
                ["contrato"] = nameof(NotificacionPendienteV1)
            },
            ct);
    }

    /// <summary>Publica un mensaje en notificaciones.dlq con el motivo y la fecha de rechazo.</summary>
    /// <param name="clave">Clave del mensaje (el número de guía).</param>
    /// <param name="contenido">Contenido original o la notificación pendiente.</param>
    /// <param name="motivo">Por qué se rechaza.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando Kafka confirmó.</returns>
    public Task EnviarADlqAsync(string clave, string? contenido, string motivo, CancellationToken ct) =>
        productor.PublicarAsync(
            opciones.TopicoDlq,
            clave,
            contenido ?? "",
            new Dictionary<string, string>
            {
                ["dlq-motivo"] = motivo,
                ["dlq-rechazado-en"] = reloj.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)
            },
            ct);

    /// <summary>Para el ConsumidorKafka de los tópicos de reintento: cuándo se puede procesar cada mensaje.</summary>
    /// <param name="mensaje">Mensaje de reintento.</param>
    /// <returns>El vencimiento, o <see langword="null"/> si el mensaje no lo trae (se procesa de inmediato).</returns>
    public static DateTimeOffset? Vencimiento(MensajeKafka mensaje) =>
        DateTimeOffset.TryParse(mensaje.Encabezado(EncabezadoReintentarDespues), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var vence) ? vence : null;
}

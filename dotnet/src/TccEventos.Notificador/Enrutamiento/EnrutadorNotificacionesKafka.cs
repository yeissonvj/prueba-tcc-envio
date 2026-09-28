using System.Globalization;
using System.Text.Json;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Notificador.Enrutamiento;

public sealed class EnrutadorNotificacionesKafka(ProductorKafka productor, OpcionesNotificador opciones, TimeProvider reloj)
    : IEnrutadorNotificaciones
{
    public const string EncabezadoReintentarDespues = "reintentar-despues";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    /// Para el ConsumidorKafka de los tópicos de reintento: cuándo se puede procesar cada mensaje.
    public static DateTimeOffset? Vencimiento(MensajeKafka mensaje) =>
        DateTimeOffset.TryParse(mensaje.Encabezado(EncabezadoReintentarDespues), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var vence) ? vence : null;
}

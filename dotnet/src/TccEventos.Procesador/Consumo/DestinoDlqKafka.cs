using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.Consumo;

/// <summary>
/// Publica el mensaje original, intacto, en guias.eventos.dlq con encabezados para diagnosticarlo
/// y reinyectarlo (redrive) después de corregir la causa.
/// </summary>
/// <param name="productor">Productor durable de Kafka.</param>
/// <param name="opciones">Opciones de Kafka (tópico de la DLQ).</param>
/// <param name="reloj">Reloj para la marca de rechazo.</param>
public sealed class DestinoDlqKafka(ProductorKafka productor, OpcionesKafka opciones, TimeProvider reloj) : IDestinoDlq
{
    /// <summary>
    /// Publica el mensaje con encabezados de motivo, tópico, partición y offset de origen, y fecha de rechazo.
    /// </summary>
    /// <param name="rechazado">Mensaje original con el motivo.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando Kafka confirmó.</returns>
    public Task EnviarAsync(MensajeRechazado rechazado, CancellationToken ct)
    {
        var original = rechazado.Original;
        return productor.PublicarAsync(
            opciones.TopicoEventosDlq,
            original.Clave ?? "",
            original.Valor ?? "",
            new Dictionary<string, string>
            {
                ["dlq-motivo"] = rechazado.Motivo,
                ["dlq-topico-origen"] = original.Topico,
                ["dlq-particion-origen"] = original.Particion.ToString(),
                ["dlq-offset-origen"] = original.Offset.ToString(),
                ["dlq-rechazado-en"] = reloj.GetUtcNow().ToString("O")
            },
            ct);
    }
}

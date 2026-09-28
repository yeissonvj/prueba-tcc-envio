using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.Consumo;

/// Publica el mensaje original, intacto, en guias.eventos.dlq con encabezados para diagnosticarlo
/// y reinyectarlo (redrive) después de corregir la causa.
public sealed class DestinoDlqKafka(ProductorKafka productor, OpcionesKafka opciones, TimeProvider reloj) : IDestinoDlq
{
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

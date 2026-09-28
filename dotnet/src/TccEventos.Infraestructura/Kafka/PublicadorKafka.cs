using System.Text.Json;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Infraestructura.Kafka;

/// Adaptador del puerto IPublicadorEventos: serializa el evento al contrato V1 y lo publica
/// en guias.eventos.recibidos con el número de guía como clave (orden por guía).
public sealed class PublicadorKafka(ProductorKafka productor, OpcionesKafka opciones) : IPublicadorEventos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task PublicarAsync(EventoGuia evento, CancellationToken ct) =>
        productor.PublicarAsync(
            opciones.TopicoEventosRecibidos,
            evento.NumeroGuia,
            JsonSerializer.Serialize(MapeadorEventoGuia.AContrato(evento), Json),
            new Dictionary<string, string>
            {
                ["idEvento"] = evento.IdEvento.ToString(),
                ["contrato"] = nameof(EventoGuiaV1)
            },
            ct);
}

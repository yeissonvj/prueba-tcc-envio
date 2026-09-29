using System.Text.Json;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Adaptador del puerto <see cref="IPublicadorEventos"/>: serializa el evento al contrato V1 y lo publica
/// en guias.eventos.recibidos con el número de guía como clave (orden por guía).
/// </summary>
/// <param name="productor">Productor durable compartido.</param>
/// <param name="opciones">Opciones de Kafka (nombre del tópico).</param>
public sealed class PublicadorKafka(ProductorKafka productor, OpcionesKafka opciones) : IPublicadorEventos
{
    /// <summary>Reglas de serialización web (camelCase).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Publica el evento en guias.eventos.recibidos.</summary>
    /// <param name="evento">Evento a publicar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando Kafka confirmó.</returns>
    /// <exception cref="PublicacionFallidaException">Kafka no confirmó el mensaje.</exception>
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

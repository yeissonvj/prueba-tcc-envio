using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>
/// Caso de uso: recibe un evento de un sistema origen y lo deja guardado de forma durable.
/// Lo usa la API de ingesta.
/// </summary>
/// <param name="publicador">Publicador durable (Kafka con contingencia).</param>
/// <param name="filtro">Filtro rápido de duplicados (Redis, tolerante a fallas).</param>
public class RecibirEvento(IPublicadorEventos publicador, IFiltroDuplicados filtro)
{
    /// <summary>Recibe un evento: si ya se había recibido no hace nada; si no, lo publica y lo marca.</summary>
    /// <param name="evento">Evento validado en la frontera.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see cref="ResultadoRecepcion.Aceptado"/> (→ 202) o <see cref="ResultadoRecepcion.Duplicado"/> (→ 200).</returns>
    /// <exception cref="PublicacionFallidaException">El evento no quedó durable en ningún lado (→ 503).</exception>
    public async Task<ResultadoRecepcion> EjecutarAsync(EventoGuia evento, CancellationToken ct)
    {
        if (await filtro.YaRecibidoAsync(evento.IdEvento, ct))
            return ResultadoRecepcion.Duplicado;

        await publicador.PublicarAsync(evento, ct);

        // Se marca DESPUÉS de publicar: si la publicación falla,
        // un reintento del emisor no debe ser descartado como duplicado.
        await filtro.MarcarRecibidoAsync(evento.IdEvento, ct);

        return ResultadoRecepcion.Aceptado;
    }
}

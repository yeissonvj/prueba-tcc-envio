using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>
/// Recibe un evento de un sistema origen y lo deja guardado de forma durable.
/// </summary>
public class RecibirEvento(IPublicadorEventos publicador, IFiltroDuplicados filtro)
{
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
using Microsoft.Extensions.Logging;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Decoradores;

/// Patrón Decorador: si el publicador principal no confirma, el evento se guarda en la contingencia.
/// Regla de oro: nunca 202 sin almacenamiento durable; si ninguno lo guarda, se propaga la falla (→ 503).
public class PublicadorConContingencia(
    IPublicadorEventos principal,
    IAlmacenContingencia contingencia,
    ILogger<PublicadorConContingencia> logger) : IPublicadorEventos
{
    public async Task PublicarAsync(EventoGuia evento, CancellationToken ct)
    {
        try
        {
            await principal.PublicarAsync(evento, ct);
            return;
        }
        catch (PublicacionFallidaException ex)
        {
            // Debug y no Warning: durante una caída serían miles por segundo.
            // La caída en sí la reporta el circuito al abrirse.
            logger.LogDebug(ex, "Publicador principal no disponible; {IdEvento} va a contingencia", evento.IdEvento);
        }

        try
        {
            await contingencia.GuardarAsync(evento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PublicacionFallidaException(
                $"Ni el publicador principal ni la contingencia guardaron el evento {evento.IdEvento}.", ex);
        }
    }
}

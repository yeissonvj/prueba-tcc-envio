using Microsoft.Extensions.Logging;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Decoradores;

/// <summary>
/// Patrón Decorador: si el publicador principal no confirma, el evento se guarda en la contingencia.
/// </summary>
/// <remarks>
/// Regla de oro: nunca 202 sin almacenamiento durable; si ninguno lo guarda, se propaga la falla (→ 503).
/// </remarks>
/// <param name="principal">Publicador principal (Kafka con circuito).</param>
/// <param name="contingencia">Almacén de respaldo (PostgreSQL).</param>
/// <param name="logger">Registro de diagnóstico.</param>
public class PublicadorConContingencia(
    IPublicadorEventos principal,
    IAlmacenContingencia contingencia,
    ILogger<PublicadorConContingencia> logger) : IPublicadorEventos
{
    /// <summary>Publica en Kafka y, si falla, guarda en la contingencia.</summary>
    /// <param name="evento">Evento a guardar de forma durable.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina bien solo si el evento quedó guardado en alguno de los dos.</returns>
    /// <exception cref="PublicacionFallidaException">Ni Kafka ni la contingencia guardaron el evento.</exception>
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

using Microsoft.Extensions.Logging;

using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.Decoradores;

/// <summary>
/// Patrón Decorador: envuelve cualquier <see cref="IFiltroDuplicados"/> y, si falla, la recepción continúa.
/// </summary>
/// <remarks>
/// El filtro es una optimización; la garantía de no duplicar está en el inbox del procesador.
/// </remarks>
/// <param name="interno">Filtro real (Redis).</param>
/// <param name="logger">Registro de advertencias cuando el filtro falla.</param>
public class FiltroDuplicadosTolerante(IFiltroDuplicados interno, ILogger<FiltroDuplicadosTolerante> logger)
    : IFiltroDuplicados
{
    /// <summary>Consulta el filtro; si no responde, considera el evento como nuevo.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Lo que responda el filtro, o <see langword="false"/> si falló.</returns>
    public async Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct)
    {
        try
        {
            return await interno.YaRecibidoAsync(idEvento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ante la duda, "no recibido": se publica de nuevo y el procesador deduplica.
            // Responder "recibido" sin estar seguros podría PERDER el evento.
            logger.LogWarning(ex, "Filtro de duplicados no disponible al consultar {IdEvento}; se continúa sin filtro", idEvento);
            return false;
        }
    }

    /// <summary>Marca el evento como recibido; si el filtro falla, solo registra una advertencia.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina siempre, aunque el filtro falle.</returns>
    public async Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct)
    {
        try
        {
            await interno.MarcarRecibidoAsync(idEvento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El evento YA es durable en Kafka: no marcarlo solo cuesta un posible duplicado.
            logger.LogWarning(ex, "No se pudo marcar {IdEvento} en el filtro de duplicados", idEvento);
        }
    }
}

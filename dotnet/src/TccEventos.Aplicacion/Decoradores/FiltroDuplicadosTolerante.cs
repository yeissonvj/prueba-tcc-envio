using Microsoft.Extensions.Logging;

using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.Decoradores;

/// Patrón Decorador: envuelve cualquier IFiltroDuplicados y, si falla, la recepción continúa.
/// El filtro es una optimización; la garantía de no duplicar está en el inbox del procesador.
public class FiltroDuplicadosTolerante(IFiltroDuplicados interno, ILogger<FiltroDuplicadosTolerante> logger)
    : IFiltroDuplicados
{
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
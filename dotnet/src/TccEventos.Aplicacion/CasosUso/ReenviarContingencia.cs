using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>
/// Caso de uso: vacía la contingencia hacia el broker cuando vuelve a estar disponible.
/// </summary>
/// <remarks>
/// El publicador que recibe debe ser el directo (sin contingencia): si no, un evento que falla
/// volvería a la misma tabla de la que salió.
/// </remarks>
/// <param name="almacen">Almacén de contingencia (PostgreSQL).</param>
/// <param name="publicadorDirecto">Publicador hacia Kafka, sin contingencia.</param>
public class ReenviarContingencia(IAlmacenContingencia almacen, IPublicadorEventos publicadorDirecto)
{
    /// <summary>Reenvía un lote de eventos pendientes, en orden de llegada.</summary>
    /// <param name="maximo">Tamaño máximo del lote.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Cuántos eventos se reenviaron; los que fallan quedan para el siguiente ciclo.</returns>
    public Task<int> EjecutarAsync(int maximo, CancellationToken ct) =>
        almacen.ReenviarPendientesAsync(publicadorDirecto.PublicarAsync, maximo, ct);
}

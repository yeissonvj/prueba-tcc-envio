using TccEventos.Contratos.V1;

namespace TccEventos.Infraestructura.Consultas;

/// <summary>
/// Lado de lectura (CQRS ligero): devuelve el contrato directamente, sin pasar por el dominio,
/// porque leer no cambia nada.
/// </summary>
/// <remarks>Hoy lee PostgreSQL; a escala, una proyección en Redis.</remarks>
public interface IConsultaGuias
{
    /// <summary>Obtiene el estado actual y el historial reciente de una guía.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>La guía, o <see langword="null"/> si no hay eventos procesados.</returns>
    Task<GuiaV1?> ObtenerAsync(string numeroGuia, CancellationToken ct);
}

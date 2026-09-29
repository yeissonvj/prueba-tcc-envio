using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

/// <summary>
/// Verificación de "lista" del filtro de duplicados: el filtro es una optimización; si no está,
/// la API funciona igual (degradada), nunca "no lista".
/// </summary>
/// <param name="redis">Sonda de Redis.</param>
public sealed class VerificacionFiltroDuplicados(ISonda redis) : IHealthCheck
{
    /// <summary>Consulta la sonda de Redis.</summary>
    /// <param name="context">Contexto de la verificación.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Healthy si Redis responde; Degraded si no.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await redis.DisponibleAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Filtro de duplicados no disponible: el procesador deduplica.");
}

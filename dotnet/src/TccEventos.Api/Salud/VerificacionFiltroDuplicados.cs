using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

/// El filtro es una optimización: si no está, la API funciona igual (degradada), nunca "no lista".
public sealed class VerificacionFiltroDuplicados(ISonda redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await redis.DisponibleAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Filtro de duplicados no disponible: el procesador deduplica.");
}

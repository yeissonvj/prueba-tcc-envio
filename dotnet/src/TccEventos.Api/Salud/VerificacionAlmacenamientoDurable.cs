using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

/// Regla: la API está lista mientras pueda guardar de forma durable en ALGÚN lado.
/// Kafka caído con contingencia disponible = degradada pero lista (sigue respondiendo 202 con garantía).
public sealed class VerificacionAlmacenamientoDurable(ISonda kafka, ISonda contingencia) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var revisionKafka = kafka.DisponibleAsync(ct);
        var revisionContingencia = contingencia.DisponibleAsync(ct);
        var (kafkaDisponible, contingenciaDisponible) = (await revisionKafka, await revisionContingencia);

        return (kafkaDisponible, contingenciaDisponible) switch
        {
            (true, true) => HealthCheckResult.Healthy(),
            (false, true) => HealthCheckResult.Degraded("Kafka no disponible: se opera en contingencia."),
            (true, false) => HealthCheckResult.Degraded("Contingencia no disponible: sin respaldo si Kafka falla."),
            (false, false) => HealthCheckResult.Unhealthy("Ni Kafka ni la contingencia disponibles.")
        };
    }
}

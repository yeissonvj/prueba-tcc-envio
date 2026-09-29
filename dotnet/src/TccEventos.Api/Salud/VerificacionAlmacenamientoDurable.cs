using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

/// <summary>
/// Verificación de "lista": la API está lista mientras pueda guardar de forma durable en ALGÚN lado.
/// </summary>
/// <remarks>
/// Kafka caído con contingencia disponible = degradada pero lista (sigue respondiendo 202 con garantía).
/// </remarks>
/// <param name="kafka">Sonda de Kafka.</param>
/// <param name="contingencia">Sonda de la contingencia (PostgreSQL).</param>
public sealed class VerificacionAlmacenamientoDurable(ISonda kafka, ISonda contingencia) : IHealthCheck
{
    /// <summary>Consulta ambas sondas en paralelo y combina sus resultados.</summary>
    /// <param name="context">Contexto de la verificación.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// Healthy si ambos están bien; Degraded si solo uno lo está; Unhealthy si ninguno (la instancia sale del balanceador).
    /// </returns>
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

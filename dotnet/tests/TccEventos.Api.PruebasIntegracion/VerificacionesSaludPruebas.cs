using Microsoft.Extensions.Diagnostics.HealthChecks;
using TccEventos.Api.PruebasIntegracion.Falsos;
using TccEventos.Api.Salud;

namespace TccEventos.Api.PruebasIntegracion;

public class VerificacionesSaludPruebas
{
    private static Task<HealthCheckResult> Revisar(IHealthCheck verificacion) =>
        verificacion.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    [Theory]
    [InlineData(true, true, HealthStatus.Healthy)]
    [InlineData(false, true, HealthStatus.Degraded)]    // opera en contingencia: sigue siendo durable
    [InlineData(true, false, HealthStatus.Degraded)]    // funciona, pero sin respaldo
    [InlineData(false, false, HealthStatus.Unhealthy)]  // solo podría responder 503
    public async Task La_api_esta_lista_mientras_pueda_guardar_de_forma_durable_en_algun_lado(
        bool kafka, bool contingencia, HealthStatus esperado)
    {
        var verificacion = new VerificacionAlmacenamientoDurable(new SondaFalsa(kafka), new SondaFalsa(contingencia));

        Assert.Equal(esperado, (await Revisar(verificacion)).Status);
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Degraded)]
    public async Task Sin_filtro_de_duplicados_la_api_queda_degradada_pero_nunca_fuera(bool redis, HealthStatus esperado)
    {
        var verificacion = new VerificacionFiltroDuplicados(new SondaFalsa(redis));

        Assert.Equal(esperado, (await Revisar(verificacion)).Status);
    }
}

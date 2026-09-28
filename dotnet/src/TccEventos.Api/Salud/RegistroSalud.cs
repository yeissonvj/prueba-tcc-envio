using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

public static class RegistroSalud
{
    public const string SondaKafka = "kafka";
    public const string SondaContingencia = "contingencia";
    public const string SondaFiltro = "filtro";
    private const string EtiquetaLista = "lista";

    public static IServiceCollection AgregarSalud(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ISonda, SondaKafka>(SondaKafka);
        services.AddKeyedSingleton<ISonda, SondaPostgres>(SondaContingencia);
        services.AddKeyedSingleton<ISonda, SondaRedis>(SondaFiltro);

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "almacenamiento-durable",
                sp => new VerificacionAlmacenamientoDurable(
                    sp.GetRequiredKeyedService<ISonda>(SondaKafka),
                    sp.GetRequiredKeyedService<ISonda>(SondaContingencia)),
                failureStatus: null,
                tags: [EtiquetaLista]))
            .Add(new HealthCheckRegistration(
                "filtro-duplicados",
                sp => new VerificacionFiltroDuplicados(sp.GetRequiredKeyedService<ISonda>(SondaFiltro)),
                failureStatus: null,
                tags: [EtiquetaLista]));

        return services;
    }

    public static IEndpointRouteBuilder MapSalud(this IEndpointRouteBuilder app)
    {
        // Vida: sin dependencias externas. Si revisara Kafka, una caída de Kafka reiniciaría todos los pods.
        app.MapHealthChecks("/salud/viva", new HealthCheckOptions { Predicate = _ => false });

        // Lista: Degraded sigue recibiendo tráfico (200); solo Unhealthy lo saca del balanceador (503).
        // La respuesta es solo el estado: qué dependencia falló no se expone.
        app.MapHealthChecks("/salud/lista", new HealthCheckOptions
        {
            Predicate = registro => registro.Tags.Contains(EtiquetaLista),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        return app;
    }
}

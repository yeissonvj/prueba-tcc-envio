using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TccEventos.Api.Salud;

/// <summary>Sondas de salud de la API: /salud/viva y /salud/lista.</summary>
public static class RegistroSalud
{
    /// <summary>Clave de la sonda de Kafka en el contenedor de servicios.</summary>
    public const string SondaKafka = "kafka";

    /// <summary>Clave de la sonda de la contingencia (PostgreSQL).</summary>
    public const string SondaContingencia = "contingencia";

    /// <summary>Clave de la sonda del filtro de duplicados (Redis).</summary>
    public const string SondaFiltro = "filtro";

    /// <summary>Etiqueta de las verificaciones que cuentan para /salud/lista.</summary>
    private const string EtiquetaLista = "lista";

    /// <summary>Registra las tres sondas y las dos verificaciones de "lista".</summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
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

    /// <summary>
    /// Publica /salud/viva (sin dependencias externas) y /salud/lista (200 si Healthy o Degraded, 503 si Unhealthy).
    /// </summary>
    /// <param name="app">Constructor de rutas.</param>
    /// <returns>El mismo constructor, para encadenar llamadas.</returns>
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

using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace TccEventos.Api.Configuracion;

/// <summary>
/// Documentación interactiva de la API: OpenAPI + Scalar.
/// </summary>
/// <remarks>
/// Se publica solo si Documentacion:Habilitada = true (encendido en Desarrollo):
/// exponer la documentación en producción es entregar el mapa de la API.
/// </remarks>
public static class RegistroDocumentacion
{
    /// <summary>Clave de configuración que habilita la documentación.</summary>
    private const string Clave = "Documentacion:Habilitada";

    /// <summary>Registra la generación del documento OpenAPI con el título y la descripción de la API.</summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    public static IServiceCollection AgregarDocumentacion(this IServiceCollection services) =>
        services.AddOpenApi(opciones => opciones.AddDocumentTransformer((documento, _, _) =>
        {
            documento.Info = new OpenApiInfo
            {
                Title = "TCC · API de ingesta de eventos de guía",
                Version = "v1",
                Description = """
                    Recibe eventos de estado de guías y los guarda de forma durable (Kafka, o contingencia
                    si Kafka no responde) antes de confirmar. Reintentar siempre con el mismo idEvento:
                    un duplicado responde 200 sin efecto adicional.
                    """
            };
            return Task.CompletedTask;
        }));

    /// <summary>Publica /openapi/v1.json y /scalar/v1, solo si la documentación está habilitada.</summary>
    /// <param name="app">Aplicación web.</param>
    /// <returns>La misma aplicación, para encadenar llamadas.</returns>
    public static WebApplication MapDocumentacion(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(Clave))
            return app;

        app.MapOpenApi();                 // /openapi/v1.json
        app.MapScalarApiReference(o => o // /scalar/v1
            .WithTitle("TCC · API de ingesta")
            .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));

        return app;
    }
}

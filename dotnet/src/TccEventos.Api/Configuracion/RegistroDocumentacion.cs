using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace TccEventos.Api.Configuracion;

/// OpenAPI + Scalar. Se publica solo si Documentacion:Habilitada = true (encendido en Desarrollo):
/// exponer la documentación en producción es entregar el mapa de la API.
public static class RegistroDocumentacion
{
    private const string Clave = "Documentacion:Habilitada";

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

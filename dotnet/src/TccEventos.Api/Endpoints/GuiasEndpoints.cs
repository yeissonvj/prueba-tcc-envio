using Microsoft.AspNetCore.Http.HttpResults;
using TccEventos.Api.Seguridad;
using TccEventos.Api.Validacion;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Consultas;

namespace TccEventos.Api.Endpoints;

public static class GuiasEndpoints
{
    public static IEndpointRouteBuilder MapGuias(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/guias/{numeroGuia}", ObtenerAsync)
            .RequireAuthorization(Politicas.LeerGuias)
            .RequireRateLimiting(Politicas.LimitePorCliente)
            .WithName("ObtenerGuia")
            .WithTags("Guías")
            .WithSummary("Consulta el estado actual y el historial de una guía")
            .WithDescription("""
                Requiere un token OAuth2 con el alcance guias:leer.
                Consistencia eventual: tras un 202 de la ingesta, el cambio aparece en segundos (SLO p95 < 5 s);
                mientras tanto puede responder 404 o el estado anterior.
                El historial incluye los 100 eventos más recientes, también los tardíos y los inválidos.
                """)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<Results<Ok<GuiaV1>, ValidationProblem, ProblemHttpResult>> ObtenerAsync(
        string numeroGuia,
        IConsultaGuias consulta,
        HttpContext http,
        CancellationToken ct)
    {
        if (!ValidadorEventoGuiaV1.EsNumeroGuiaValido(numeroGuia))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["numeroGuia"] = ["Solo letras y números, máximo 30 caracteres."] },
                title: "Número de guía inválido");

        // Datos de envíos de clientes: ningún proxy o navegador intermedio debe guardarlos.
        http.Response.Headers.CacheControl = "no-store";

        var guia = await consulta.ObtenerAsync(numeroGuia, ct);
        return guia is null
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Guía no encontrada",
                detail: "No hay eventos procesados para esa guía. Si se acaba de reportar, reintente en unos segundos.")
            : TypedResults.Ok(guia);
    }
}

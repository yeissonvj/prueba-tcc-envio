using Microsoft.AspNetCore.Http.HttpResults;
using TccEventos.Api.Seguridad;
using TccEventos.Api.Validacion;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Consultas;

namespace TccEventos.Api.Endpoints;

/// <summary>Endpoint de consulta: GET /api/v1/guias/{numeroGuia}.</summary>
public static class GuiasEndpoints
{
    /// <summary>
    /// Publica la ruta con su autorización (alcance guias:leer), su límite por cliente
    /// y la descripción de sus respuestas para OpenAPI.
    /// </summary>
    /// <param name="app">Constructor de rutas.</param>
    /// <returns>El mismo constructor, para encadenar llamadas.</returns>
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

    /// <summary>
    /// Devuelve el estado actual y el historial de la guía, sin permitir que se guarde en caché.
    /// </summary>
    /// <param name="numeroGuia">Número de la guía (de la ruta).</param>
    /// <param name="consulta">Lado de lectura (CQRS ligero).</param>
    /// <param name="http">Contexto HTTP, para el encabezado Cache-Control.</param>
    /// <param name="ct">Token de cancelación de la petición.</param>
    /// <returns>200 con la guía, 400 si el número está mal formado (sin consultar la base) o 404 si no existe.</returns>
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

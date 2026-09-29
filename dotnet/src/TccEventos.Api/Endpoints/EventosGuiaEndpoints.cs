using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TccEventos.Api.Seguridad;
using TccEventos.Api.Validacion;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Mapeo;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Api.Endpoints;

/// <summary>Endpoint de ingesta: POST /api/v1/eventos-guia.</summary>
public static class EventosGuiaEndpoints
{
    /// <summary>
    /// Publica la ruta con su autorización (alcance eventos:escribir), su límite por cliente
    /// y la descripción de todas sus respuestas para OpenAPI.
    /// </summary>
    /// <param name="app">Constructor de rutas.</param>
    /// <returns>El mismo constructor, para encadenar llamadas.</returns>
    public static IEndpointRouteBuilder MapEventosGuia(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/eventos-guia", RecibirAsync)
            .RequireAuthorization(Politicas.EscribirEventos)
            .RequireRateLimiting(Politicas.LimitePorCliente)
            .WithName("RecibirEventoGuia")
            .WithTags("Eventos de guía")
            .WithSummary("Recibe un evento de estado de guía")
            .WithDescription("""
                Requiere un token OAuth2 (client credentials) con el alcance eventos:escribir.
                202: guardado de forma durable; se procesará en segundos (consultar la URL de Location).
                200: el idEvento ya se había recibido; no tiene efecto adicional.
                400: no cumple el contrato V1; no reintentar sin corregir.
                401: sin token o token inválido (firma, emisor, audiencia o vigencia).
                403: el token no tiene eventos:escribir, o el cliente no puede reportar ese origen.
                413: el cuerpo supera 64 KB.
                429: se superó el límite de peticiones del cliente; reintentar tras Retry-After.
                503: no se pudo guardar de forma durable; reintentar con el mismo idEvento tras Retry-After.
                """)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Recibe un evento: valida el contrato, verifica que el cliente pueda reportar ese origen
    /// y lo entrega al caso de uso <see cref="RecibirEvento"/>.
    /// </summary>
    /// <param name="evento">Cuerpo de la petición.</param>
    /// <param name="usuario">Cliente autenticado (claims del token).</param>
    /// <param name="validador">Validador del contrato V1.</param>
    /// <param name="autorizadorOrigen">Regla anti-suplantación por origen.</param>
    /// <param name="recibirEvento">Caso de uso de recepción.</param>
    /// <param name="ct">Token de cancelación de la petición.</param>
    /// <returns>202 (aceptado, con Location), 200 (duplicado), 400 (contrato) o 403 (origen no permitido).</returns>
    /// <exception cref="Aplicacion.Puertos.PublicacionFallidaException">
    /// Nada quedó durable; el manejador de errores la convierte en 503.
    /// </exception>
    private static async Task<Results<Accepted<RespuestaRecepcionV1>, Ok<RespuestaRecepcionV1>, ValidationProblem, ProblemHttpResult>> RecibirAsync(
        EventoGuiaV1 evento,
        ClaimsPrincipal usuario,
        ValidadorEventoGuiaV1 validador,
        AutorizadorOrigen autorizadorOrigen,
        RecibirEvento recibirEvento,
        CancellationToken ct)
    {
        var errores = validador.Validar(evento);
        if (errores.Count > 0)
        {
            Telemetria.EventosRecibidos.Add(1, Telemetria.Etiqueta("resultado", "rechazado"));
            return TypedResults.ValidationProblem(errores, title: "El evento no cumple el contrato V1");
        }

        if (!autorizadorOrigen.PuedeReportar(usuario, evento.Origen))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Origen no permitido",
                detail: "El cliente autenticado no puede reportar eventos con ese origen.");

        var resultado = await recibirEvento.EjecutarAsync(MapeadorEventoGuia.ADominio(evento), ct);
        Telemetria.EventosRecibidos.Add(1, Telemetria.Etiqueta("resultado", Telemetria.Texto(resultado)));

        return resultado switch
        {
            ResultadoRecepcion.Aceptado => TypedResults.Accepted(
                $"/api/v1/guias/{evento.NumeroGuia}",
                new RespuestaRecepcionV1(evento.IdEvento, RespuestaRecepcionV1.Aceptado)),
            ResultadoRecepcion.Duplicado => TypedResults.Ok(
                new RespuestaRecepcionV1(evento.IdEvento, RespuestaRecepcionV1.Duplicado)),
            _ => throw new UnreachableException($"Resultado de recepción no contemplado: {resultado}")
        };
    }
}

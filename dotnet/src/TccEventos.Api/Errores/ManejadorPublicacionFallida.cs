using Microsoft.AspNetCore.Diagnostics;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Api.Errores;

/// Traduce "el evento no quedó durable" a 503 + Retry-After.
/// El detalle técnico (broker, causa) va al log, nunca al cliente.
public sealed class ManejadorPublicacionFallida(
    IProblemDetailsService problemDetails,
    ILogger<ManejadorPublicacionFallida> logger) : IExceptionHandler
{
    public const int SegundosParaReintentar = 5;

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken ct)
    {
        if (excepcion is not PublicacionFallidaException)
            return false;

        // En .NET 10 el middleware ya no registra las excepciones que un IExceptionHandler resuelve:
        // si no se registra aquí, una caída de Kafka no dejaría rastro.
        logger.LogError(excepcion, "Evento no durable; se responde 503");
        Telemetria.EventosRecibidos.Add(1, Telemetria.Etiqueta("resultado", "no_durable"));

        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.Headers.RetryAfter = SegundosParaReintentar.ToString();

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Servicio temporalmente no disponible",
                Detail = "El evento no pudo guardarse de forma durable. Reintente con el mismo idEvento."
            }
        });
    }
}

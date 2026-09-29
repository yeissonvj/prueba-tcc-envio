using Microsoft.AspNetCore.Diagnostics;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Api.Errores;

/// <summary>
/// Eslabón de la cadena de manejadores de errores: traduce "el evento no quedó durable" a 503 + Retry-After.
/// </summary>
/// <remarks>El detalle técnico (broker, causa) va al log, nunca al cliente.</remarks>
/// <param name="problemDetails">Servicio que escribe la respuesta como ProblemDetails.</param>
/// <param name="logger">Registro del error.</param>
public sealed class ManejadorPublicacionFallida(
    IProblemDetailsService problemDetails,
    ILogger<ManejadorPublicacionFallida> logger) : IExceptionHandler
{
    /// <summary>Segundos que se le piden al emisor esperar antes de reintentar.</summary>
    public const int SegundosParaReintentar = 5;

    /// <summary>Atiende solo <see cref="PublicacionFallidaException"/>; cualquier otro error lo deja pasar.</summary>
    /// <param name="http">Contexto de la petición.</param>
    /// <param name="excepcion">Error ocurrido.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si respondió 503; <see langword="false"/> si no es su tipo de error.</returns>
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

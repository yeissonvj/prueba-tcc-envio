using Microsoft.AspNetCore.Diagnostics;

namespace TccEventos.Api.Errores;

/// JSON mal formado, tipos incorrectos (p. ej. "idEvento": 123) o cuerpo demasiado grande → 4xx, no 500.
/// No se devuelve el mensaje del parser: puede revelar detalles internos.
public sealed class ManejadorPeticionInvalida(
    IProblemDetailsService problemDetails,
    ILogger<ManejadorPeticionInvalida> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken ct)
    {
        if (excepcion is not BadHttpRequestException peticionInvalida)
            return false;

        // Error del emisor, no del sistema: Information, para no inundar el log si alguien envía basura.
        logger.LogInformation(excepcion, "Petición rechazada con {Estado}", peticionInvalida.StatusCode);

        var demasiadoGrande = peticionInvalida.StatusCode == StatusCodes.Status413PayloadTooLarge;
        http.Response.StatusCode = peticionInvalida.StatusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = peticionInvalida.StatusCode,
                Title = demasiadoGrande ? "Cuerpo demasiado grande" : "Petición inválida",
                Detail = demasiadoGrande
                    ? "El cuerpo supera el tamaño máximo permitido."
                    : "El cuerpo no es un JSON válido para el contrato EventoGuiaV1."
            }
        });
    }
}

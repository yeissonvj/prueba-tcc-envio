using Microsoft.AspNetCore.Diagnostics;

namespace TccEventos.Api.Errores;

/// <summary>
/// Eslabón de la cadena de manejadores de errores: JSON mal formado, tipos incorrectos
/// (por ejemplo "idEvento": 123) o cuerpo demasiado grande → 4xx, no 500.
/// </summary>
/// <remarks>No se devuelve el mensaje del parser: puede revelar detalles internos.</remarks>
/// <param name="problemDetails">Servicio que escribe la respuesta como ProblemDetails.</param>
/// <param name="logger">Registro de las peticiones rechazadas.</param>
public sealed class ManejadorPeticionInvalida(
    IProblemDetailsService problemDetails,
    ILogger<ManejadorPeticionInvalida> logger) : IExceptionHandler
{
    /// <summary>Atiende solo las peticiones inválidas; cualquier otro error lo deja pasar al siguiente manejador.</summary>
    /// <param name="http">Contexto de la petición.</param>
    /// <param name="excepcion">Error ocurrido.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si respondió 400 o 413; <see langword="false"/> si no es su tipo de error.</returns>
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

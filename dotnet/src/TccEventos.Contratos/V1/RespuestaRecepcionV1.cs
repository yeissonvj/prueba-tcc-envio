namespace TccEventos.Contratos.V1;

/// <summary>
/// Cuerpo de la respuesta de POST /api/v1/eventos-guia cuando el evento se recibe (202) o ya se había recibido (200).
/// </summary>
/// <param name="IdEvento">Identificador del evento recibido.</param>
/// <param name="Resultado"><see cref="Aceptado"/> o <see cref="Duplicado"/>.</param>
public record RespuestaRecepcionV1(Guid IdEvento, string Resultado)
{
    /// <summary>El evento quedó guardado de forma durable y se procesará (202).</summary>
    public const string Aceptado = "ACEPTADO";

    /// <summary>El evento ya se había recibido; no tiene efecto adicional (200).</summary>
    public const string Duplicado = "DUPLICADO";
}

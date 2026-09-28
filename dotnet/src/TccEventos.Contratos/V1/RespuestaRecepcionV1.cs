namespace TccEventos.Contratos.V1;

public record RespuestaRecepcionV1(Guid IdEvento, string Resultado)
{
    public const string Aceptado = "ACEPTADO";
    public const string Duplicado = "DUPLICADO";
}

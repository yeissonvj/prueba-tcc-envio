namespace TccEventos.Aplicacion.CasosUso;

/// <summary>Qué pasó al recibir un evento en la API.</summary>
public enum ResultadoRecepcion
{
    /// <summary>El evento quedó guardado de forma durable (la API responde 202).</summary>
    Aceptado,

    /// <summary>El evento ya se había recibido (la API responde 200 sin efecto adicional).</summary>
    Duplicado
}

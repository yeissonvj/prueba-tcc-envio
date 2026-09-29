namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// El evento NO quedó guardado de forma durable. Quien llama no debe confirmar la recepción.
/// </summary>
/// <remarks>En la API se traduce a 503 + Retry-After para que el emisor reintente.</remarks>
/// <param name="mensaje">Descripción de la falla.</param>
/// <param name="causa">Excepción que la originó, si la hay.</param>
public class PublicacionFallidaException(string mensaje, Exception? causa = null)
    : Exception(mensaje, causa);

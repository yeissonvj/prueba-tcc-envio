namespace TccEventos.Aplicacion.Puertos;

/// El evento NO quedó guardado de forma durable. Quien llama no debe confirmar la recepción.
public class PublicacionFallidaException(string mensaje, Exception? causa = null)
    : Exception(mensaje, causa);
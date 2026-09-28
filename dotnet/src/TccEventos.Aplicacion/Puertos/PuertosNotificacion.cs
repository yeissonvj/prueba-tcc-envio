using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

public interface IDirectorioContactos
{
    Task<Contacto?> ObtenerAsync(string numeroGuia, CancellationToken ct);
}

public record MensajeNotificacion(string ClaveIdempotencia, string Destino, string Texto);

/// Proveedor externo (SMS, correo). Debe usar ClaveIdempotencia como llave de idempotencia del proveedor.
/// Falla con ProveedorNoDisponibleException (timeout, 5xx, 429: reintentar) o
/// DestinoRechazadoException (número o correo inválido: reintentar no sirve).
public interface IProveedorNotificacion
{
    CanalNotificacion Canal { get; }
    Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct);
}

public enum EstadoNotificacion { Pendiente, YaEnviada, Obsoleta }

public interface IRegistroNotificaciones
{
    /// YaEnviada si esa clave ya se envió; Obsoleta si ya se notificó una versión más reciente de la guía.
    Task<EstadoNotificacion> ConsultarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct);
    Task RegistrarEnvioAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct);
}

public class ProveedorNoDisponibleException(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

public class DestinoRechazadoException(string mensaje) : Exception(mensaje);

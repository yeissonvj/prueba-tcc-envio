using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>Puerto: obtiene los datos de contacto del destinatario de una guía.</summary>
public interface IDirectorioContactos
{
    /// <summary>Busca el contacto del destinatario.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>El contacto, o <see langword="null"/> si no se conoce.</returns>
    Task<Contacto?> ObtenerAsync(string numeroGuia, CancellationToken ct);
}

/// <summary>Un mensaje listo para entregar al proveedor.</summary>
/// <param name="ClaveIdempotencia">Clave única (guía:versión:canal) para que el proveedor no lo envíe dos veces.</param>
/// <param name="Destino">Teléfono o correo del cliente.</param>
/// <param name="Texto">Texto del mensaje.</param>
public record MensajeNotificacion(string ClaveIdempotencia, string Destino, string Texto);

/// <summary>
/// Puerto: proveedor externo (SMS, correo).
/// </summary>
/// <remarks>
/// Debe usar <see cref="MensajeNotificacion.ClaveIdempotencia"/> como llave de idempotencia del proveedor.
/// Falla con <see cref="ProveedorNoDisponibleException"/> (timeout, 5xx, 429: reintentar) o
/// <see cref="DestinoRechazadoException"/> (número o correo inválido: reintentar no sirve).
/// </remarks>
public interface IProveedorNotificacion
{
    /// <summary>Canal que atiende este proveedor.</summary>
    CanalNotificacion Canal { get; }

    /// <summary>Envía un mensaje al cliente.</summary>
    /// <param name="mensaje">Mensaje a enviar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el proveedor aceptó el mensaje.</returns>
    Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct);
}

/// <summary>Situación de una notificación en el registro de enviadas.</summary>
public enum EstadoNotificacion
{
    /// <summary>Todavía no se ha enviado: se puede enviar.</summary>
    Pendiente,

    /// <summary>Esa misma notificación ya se envió.</summary>
    YaEnviada,

    /// <summary>Ya se notificó una versión más reciente de la guía.</summary>
    Obsoleta
}

/// <summary>Puerto: registro de las notificaciones enviadas (idempotencia y descarte de obsoletas).</summary>
public interface IRegistroNotificaciones
{
    /// <summary>
    /// Consulta la situación de una notificación.
    /// </summary>
    /// <param name="cambio">Cambio que se quiere notificar.</param>
    /// <param name="canal">Canal por el que se enviaría.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>YaEnviada si esa clave ya se envió; Obsoleta si ya se notificó una versión más reciente de la guía.</returns>
    Task<EstadoNotificacion> ConsultarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct);

    /// <summary>Registra que la notificación se envió.</summary>
    /// <param name="cambio">Cambio notificado.</param>
    /// <param name="canal">Canal usado.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando quedó registrado.</returns>
    Task RegistrarEnvioAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct);
}

/// <summary>El proveedor no está disponible (timeout, error 5xx o 429): se debe reintentar más tarde.</summary>
/// <param name="mensaje">Descripción de la falla.</param>
/// <param name="causa">Excepción que la originó, si la hay.</param>
public class ProveedorNoDisponibleException(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>El proveedor rechazó el destino (número o correo inválido): reintentar por el mismo canal no sirve.</summary>
/// <param name="mensaje">Descripción del rechazo.</param>
public class DestinoRechazadoException(string mensaje) : Exception(mensaje);

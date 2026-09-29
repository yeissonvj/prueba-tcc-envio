namespace TccEventos.Dominio;

/// <summary>Canales por los que se puede avisar al cliente.</summary>
public enum CanalNotificacion
{
    /// <summary>Mensaje de texto al celular. Es el canal principal.</summary>
    Sms,

    /// <summary>Correo electrónico. Es el canal alterno cuando el SMS no es posible.</summary>
    Correo
}

/// <summary>
/// Un cambio de estado ya aplicado por el procesador (lo que viaja en guias.estados.cambiados).
/// </summary>
/// <param name="IdEvento">Evento que provocó el cambio.</param>
/// <param name="NumeroGuia">Guía que cambió.</param>
/// <param name="EstadoAnterior">Estado antes del cambio; <see langword="null"/> si el cambio creó la guía.</param>
/// <param name="EstadoNuevo">Estado después del cambio.</param>
/// <param name="OcurridoEn">Momento en que ocurrió el evento.</param>
/// <param name="Version">Versión de la guía tras el cambio; ordena los cambios de una misma guía.</param>
public record CambioEstadoGuia(
    Guid IdEvento,
    string NumeroGuia,
    EstadoGuia? EstadoAnterior,
    EstadoGuia EstadoNuevo,
    DateTimeOffset OcurridoEn,
    long Version);

/// <summary>
/// Datos de contacto del destinatario. No viajan en los eventos (Ley 1581): se consultan al enviar.
/// </summary>
/// <param name="Telefono">Celular para SMS; <see langword="null"/> si no está registrado.</param>
/// <param name="Correo">Correo electrónico; <see langword="null"/> si no está registrado.</param>
public record Contacto(string? Telefono, string? Correo);

/// <summary>
/// Reglas del negocio para avisar al cliente: qué estados notifican, con qué texto,
/// cómo evitar duplicados y qué canal usar si el principal falla.
/// </summary>
public static class PoliticaNotificacion
{
    // Solo lo que al cliente le importa; los movimientos internos entre bodegas no generan mensajes.
    /// <summary>Estados que generan un aviso al cliente.</summary>
    private static readonly HashSet<EstadoGuia> EstadosQueNotifican =
    [
        EstadoGuia.Recogida, EstadoGuia.EnReparto, EstadoGuia.Entregada, EstadoGuia.Novedad, EstadoGuia.Devuelta
    ];

    /// <summary>Indica si un estado debe avisarse al cliente.</summary>
    /// <param name="estado">Estado nuevo de la guía.</param>
    /// <returns><see langword="true"/> para recogida, en reparto, entregada, novedad y devuelta.</returns>
    public static bool DebeNotificar(EstadoGuia estado) => EstadosQueNotifican.Contains(estado);

    /// <summary>
    /// Clave de idempotencia de una notificación: una por guía, versión del cambio y canal,
    /// así nunca se envía dos veces la misma.
    /// </summary>
    /// <param name="cambio">Cambio que se notifica.</param>
    /// <param name="canal">Canal por el que se envía.</param>
    /// <returns>Una clave con la forma <c>numeroGuia:version:CANAL</c>, por ejemplo <c>TCC123:7:SMS</c>.</returns>
    public static string ClaveIdempotencia(CambioEstadoGuia cambio, CanalNotificacion canal) =>
        $"{cambio.NumeroGuia}:{cambio.Version}:{canal.ToString().ToUpperInvariant()}";

    /// <summary>Canal a usar si el canal indicado no funciona.</summary>
    /// <param name="canal">Canal que falló.</param>
    /// <returns><see cref="CanalNotificacion.Correo"/> para el SMS; <see langword="null"/> si no hay más canales.</returns>
    public static CanalNotificacion? CanalAlterno(CanalNotificacion canal) =>
        canal == CanalNotificacion.Sms ? CanalNotificacion.Correo : null;

    /// <summary>Texto que se envía al cliente según el estado nuevo de la guía.</summary>
    /// <param name="cambio">Cambio que se notifica.</param>
    /// <returns>El mensaje listo para enviar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el estado no es uno de los que se notifican.</exception>
    public static string Texto(CambioEstadoGuia cambio) => cambio.EstadoNuevo switch
    {
        EstadoGuia.Recogida => $"TCC: recogimos tu envío {cambio.NumeroGuia}.",
        EstadoGuia.EnReparto => $"TCC: tu envío {cambio.NumeroGuia} está en reparto y llega hoy.",
        EstadoGuia.Entregada => $"TCC: tu envío {cambio.NumeroGuia} fue entregado.",
        EstadoGuia.Novedad => $"TCC: tu envío {cambio.NumeroGuia} tiene una novedad. Consulta tcc.com.co.",
        EstadoGuia.Devuelta => $"TCC: tu envío {cambio.NumeroGuia} fue devuelto al remitente.",
        _ => throw new ArgumentOutOfRangeException(nameof(cambio), cambio.EstadoNuevo, "Estado sin notificación.")
    };
}

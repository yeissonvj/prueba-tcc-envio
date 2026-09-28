namespace TccEventos.Dominio;

public enum CanalNotificacion { Sms, Correo }

/// Un cambio de estado ya aplicado por el procesador (lo que viaja en guias.estados.cambiados).
public record CambioEstadoGuia(
    Guid IdEvento,
    string NumeroGuia,
    EstadoGuia? EstadoAnterior,
    EstadoGuia EstadoNuevo,
    DateTimeOffset OcurridoEn,
    long Version);

/// Datos de contacto del destinatario. No viajan en los eventos (Ley 1581): se consultan al enviar.
public record Contacto(string? Telefono, string? Correo);

public static class PoliticaNotificacion
{
    // Solo lo que al cliente le importa; los movimientos internos entre bodegas no generan mensajes.
    private static readonly HashSet<EstadoGuia> EstadosQueNotifican =
    [
        EstadoGuia.Recogida, EstadoGuia.EnReparto, EstadoGuia.Entregada, EstadoGuia.Novedad, EstadoGuia.Devuelta
    ];

    public static bool DebeNotificar(EstadoGuia estado) => EstadosQueNotifican.Contains(estado);

    /// Una notificación por guía, versión del cambio y canal: nunca se envía dos veces la misma.
    public static string ClaveIdempotencia(CambioEstadoGuia cambio, CanalNotificacion canal) =>
        $"{cambio.NumeroGuia}:{cambio.Version}:{canal.ToString().ToUpperInvariant()}";

    public static CanalNotificacion? CanalAlterno(CanalNotificacion canal) =>
        canal == CanalNotificacion.Sms ? CanalNotificacion.Correo : null;

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

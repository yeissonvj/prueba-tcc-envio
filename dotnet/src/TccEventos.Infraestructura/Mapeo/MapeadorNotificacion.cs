using TccEventos.Contratos.V1;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Mapeo;

/// <summary>Traduce entre los contratos de notificación y el dominio.</summary>
public static class MapeadorNotificacion
{
    /// <summary>Convierte el mensaje de guias.estados.cambiados en un cambio del dominio.</summary>
    /// <param name="c">Cambio en formato de contrato.</param>
    /// <returns>El cambio con los estados como enum.</returns>
    public static CambioEstadoGuia ADominio(EstadoGuiaCambiadoV1 c) => new(
        c.IdEvento,
        c.NumeroGuia,
        c.EstadoAnterior is null ? null : MapeadorEventoGuia.EstadoDesdeTexto(c.EstadoAnterior),
        MapeadorEventoGuia.EstadoDesdeTexto(c.EstadoNuevo),
        c.OcurridoEn,
        c.Version);

    /// <summary>Convierte un canal del dominio en su texto del contrato.</summary>
    /// <param name="canal">Canal del dominio.</param>
    /// <returns>"SMS" o "CORREO".</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el canal no es conocido.</exception>
    public static string CanalATexto(CanalNotificacion canal) => canal switch
    {
        CanalNotificacion.Sms => CanalesV1.Sms,
        CanalNotificacion.Correo => CanalesV1.Correo,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, null)
    };

    /// <summary>Convierte el texto de un canal del contrato en el canal del dominio.</summary>
    /// <param name="canal">"SMS" o "CORREO".</param>
    /// <returns>El canal del dominio.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el texto no es un canal conocido.</exception>
    public static CanalNotificacion CanalDesdeTexto(string canal) => canal switch
    {
        CanalesV1.Sms => CanalNotificacion.Sms,
        CanalesV1.Correo => CanalNotificacion.Correo,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal no reconocido.")
    };
}

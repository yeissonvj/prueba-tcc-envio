using TccEventos.Contratos.V1;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Mapeo;

public static class MapeadorNotificacion
{
    public static CambioEstadoGuia ADominio(EstadoGuiaCambiadoV1 c) => new(
        c.IdEvento,
        c.NumeroGuia,
        c.EstadoAnterior is null ? null : MapeadorEventoGuia.EstadoDesdeTexto(c.EstadoAnterior),
        MapeadorEventoGuia.EstadoDesdeTexto(c.EstadoNuevo),
        c.OcurridoEn,
        c.Version);

    public static string CanalATexto(CanalNotificacion canal) => canal switch
    {
        CanalNotificacion.Sms => CanalesV1.Sms,
        CanalNotificacion.Correo => CanalesV1.Correo,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, null)
    };

    public static CanalNotificacion CanalDesdeTexto(string canal) => canal switch
    {
        CanalesV1.Sms => CanalNotificacion.Sms,
        CanalesV1.Correo => CanalNotificacion.Correo,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal no reconocido.")
    };
}

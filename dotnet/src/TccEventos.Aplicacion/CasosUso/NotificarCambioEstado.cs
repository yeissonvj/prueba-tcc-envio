using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

public enum ResultadoNotificacion { Enviada, NoAplica, YaEnviada, Obsoleta, SinDestino }

/// Envía UNA notificación por un canal. No decide reintentos ni canal alterno: eso es política del host.
/// Las fallas del proveedor se propagan como excepción para que el host las enrute.
public class NotificarCambioEstado(
    IRegistroNotificaciones registro,
    IDirectorioContactos directorio,
    IEnumerable<IProveedorNotificacion> proveedores)
{
    private readonly Dictionary<CanalNotificacion, IProveedorNotificacion> _proveedores =
        proveedores.ToDictionary(p => p.Canal);

    public async Task<ResultadoNotificacion> EjecutarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct)
    {
        if (!PoliticaNotificacion.DebeNotificar(cambio.EstadoNuevo))
            return ResultadoNotificacion.NoAplica;

        switch (await registro.ConsultarAsync(cambio, canal, ct))
        {
            case EstadoNotificacion.YaEnviada: return ResultadoNotificacion.YaEnviada;
            case EstadoNotificacion.Obsoleta: return ResultadoNotificacion.Obsoleta;
        }

        var contacto = await directorio.ObtenerAsync(cambio.NumeroGuia, ct);
        var destino = canal == CanalNotificacion.Sms ? contacto?.Telefono : contacto?.Correo;
        if (string.IsNullOrWhiteSpace(destino))
            return ResultadoNotificacion.SinDestino;

        var mensaje = new MensajeNotificacion(
            PoliticaNotificacion.ClaveIdempotencia(cambio, canal), destino, PoliticaNotificacion.Texto(cambio));
        await _proveedores[canal].EnviarAsync(mensaje, ct);

        // Si el proceso cae aquí, el reintento reenvía con la misma clave y el proveedor lo deduplica.
        await registro.RegistrarEnvioAsync(cambio, canal, ct);
        return ResultadoNotificacion.Enviada;
    }
}

using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class RegistroNotificacionesEnMemoria : IRegistroNotificaciones
{
    public List<(string Guia, long Version, CanalNotificacion Canal)> Enviadas { get; } = [];

    public Task<EstadoNotificacion> ConsultarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct) =>
        Task.FromResult(
            Enviadas.Contains((cambio.NumeroGuia, cambio.Version, canal)) ? EstadoNotificacion.YaEnviada
            : Enviadas.Any(e => e.Guia == cambio.NumeroGuia && e.Version > cambio.Version) ? EstadoNotificacion.Obsoleta
            : EstadoNotificacion.Pendiente);

    public Task RegistrarEnvioAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct)
    {
        Enviadas.Add((cambio.NumeroGuia, cambio.Version, canal));
        return Task.CompletedTask;
    }
}

public class DirectorioFijo(Contacto? contacto) : IDirectorioContactos
{
    public Task<Contacto?> ObtenerAsync(string numeroGuia, CancellationToken ct) => Task.FromResult(contacto);
}

public class ProveedorEnMemoria(CanalNotificacion canal) : IProveedorNotificacion
{
    public CanalNotificacion Canal => canal;
    public List<MensajeNotificacion> Enviados { get; } = [];
    public Exception? Falla { get; set; }

    public Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct)
    {
        if (Falla is not null) throw Falla;
        Enviados.Add(mensaje);
        return Task.CompletedTask;
    }
}

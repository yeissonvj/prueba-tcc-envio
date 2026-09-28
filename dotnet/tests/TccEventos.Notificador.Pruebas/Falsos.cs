using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Notificador.Enrutamiento;

namespace TccEventos.Notificador.Pruebas;

public class RegistroEnMemoria : IRegistroNotificaciones
{
    public List<(string Guia, long Version, CanalNotificacion Canal)> Enviadas { get; } = [];

    public Task<EstadoNotificacion> ConsultarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct) =>
        Task.FromResult(Enviadas.Contains((cambio.NumeroGuia, cambio.Version, canal)) ? EstadoNotificacion.YaEnviada : EstadoNotificacion.Pendiente);

    public Task RegistrarEnvioAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct)
    {
        Enviadas.Add((cambio.NumeroGuia, cambio.Version, canal));
        return Task.CompletedTask;
    }
}

public class DirectorioFijo(Contacto contacto) : IDirectorioContactos
{
    public Task<Contacto?> ObtenerAsync(string numeroGuia, CancellationToken ct) => Task.FromResult<Contacto?>(contacto);
}

public class ProveedorProgramable(CanalNotificacion canal) : IProveedorNotificacion
{
    public CanalNotificacion Canal => canal;
    public List<MensajeNotificacion> Enviados { get; } = [];
    public Func<Exception>? Falla { get; set; }

    public Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct)
    {
        if (Falla is not null) throw Falla();
        Enviados.Add(mensaje);
        return Task.CompletedTask;
    }
}

public class EnrutadorEnMemoria : IEnrutadorNotificaciones
{
    public List<NotificacionPendienteV1> Reintentos { get; } = [];
    public List<(string Clave, string? Contenido, string Motivo)> Dlq { get; } = [];
    public int FallasPendientes { get; set; }

    public Task ProgramarReintentoAsync(NotificacionPendienteV1 pendiente, CancellationToken ct)
    {
        FallarSiCorresponde();
        Reintentos.Add(pendiente);
        return Task.CompletedTask;
    }

    public Task EnviarADlqAsync(string clave, string? contenido, string motivo, CancellationToken ct)
    {
        FallarSiCorresponde();
        Dlq.Add((clave, contenido, motivo));
        return Task.CompletedTask;
    }

    private void FallarSiCorresponde()
    {
        if (FallasPendientes <= 0) return;
        FallasPendientes--;
        throw new PublicacionFallidaException("Kafka no disponible (simulado)");
    }
}

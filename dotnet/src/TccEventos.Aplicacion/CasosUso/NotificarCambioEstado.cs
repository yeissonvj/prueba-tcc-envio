using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>Qué pasó al intentar notificar un cambio por un canal.</summary>
public enum ResultadoNotificacion
{
    /// <summary>El proveedor aceptó el mensaje y quedó registrado.</summary>
    Enviada,

    /// <summary>El estado no se notifica al cliente (movimiento interno).</summary>
    NoAplica,

    /// <summary>Esa misma notificación (guía, versión y canal) ya se había enviado.</summary>
    YaEnviada,

    /// <summary>Ya se notificó una versión más reciente de la guía; este mensaje viejo se descarta.</summary>
    Obsoleta,

    /// <summary>El cliente no tiene teléfono o correo para ese canal.</summary>
    SinDestino
}

/// <summary>
/// Caso de uso: envía UNA notificación por un canal.
/// </summary>
/// <remarks>
/// No decide reintentos ni canal alterno: eso es política del host (el notificador).
/// Las fallas del proveedor se propagan como excepción para que el host las enrute.
/// </remarks>
/// <param name="registro">Registro de notificaciones enviadas (idempotencia).</param>
/// <param name="directorio">Directorio de contactos de los clientes.</param>
/// <param name="proveedores">Proveedores disponibles, uno por canal.</param>
public class NotificarCambioEstado(
    IRegistroNotificaciones registro,
    IDirectorioContactos directorio,
    IEnumerable<IProveedorNotificacion> proveedores)
{
    /// <summary>Proveedores indexados por canal.</summary>
    private readonly Dictionary<CanalNotificacion, IProveedorNotificacion> _proveedores =
        proveedores.ToDictionary(p => p.Canal);

    /// <summary>Intenta notificar un cambio de estado por el canal indicado.</summary>
    /// <param name="cambio">Cambio de estado a notificar.</param>
    /// <param name="canal">Canal por el que se envía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>El resultado; <see cref="ResultadoNotificacion.Enviada"/> solo si el proveedor aceptó el mensaje.</returns>
    /// <exception cref="ProveedorNoDisponibleException">El proveedor no respondió; el host decide reintentar.</exception>
    /// <exception cref="DestinoRechazadoException">El destino es inválido; el host decide usar otro canal.</exception>
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

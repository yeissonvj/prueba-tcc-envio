using System.Text.Json;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Mapeo;
using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Infraestructura.Resiliencia;

namespace TccEventos.Notificador.Enrutamiento;

/// Política de enrutamiento del notificador. NUNCA bloquea: un proveedor caído no puede frenar
/// las notificaciones de las demás guías (al revés que el procesador, donde el orden obliga a esperar).
///   éxito                          → listo
///   proveedor no disponible        → siguiente etapa de reintento (1 min → 10 min → 1 h)
///   reintentos agotados / destino
///   rechazado / sin destino        → canal alterno (con su propia escalera) y, si no hay, DLQ
///   ilegible / error inesperado    → DLQ
public sealed class ManejadorNotificacion(
    NotificarCambioEstado notificar,
    IEnrutadorNotificaciones enrutador,
    OpcionesNotificador opciones,
    ILogger<ManejadorNotificacion> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// Desde guias.estados.cambiados: primer intento por el canal principal.
    public async Task ManejarCambioAsync(MensajeKafka mensaje, CancellationToken ct)
    {
        var cambio = Leer<EstadoGuiaCambiadoV1>(mensaje.Valor);
        if (cambio is null)
        {
            await InsistirAsync(() => enrutador.EnviarADlqAsync(mensaje.Clave ?? "", mensaje.Valor, "Mensaje ilegible", ct), ct);
            return;
        }

        await IntentarAsync(new NotificacionPendienteV1(cambio, CanalesV1.Sms, 0, mensaje.Marca), ct);
    }

    /// Desde notificaciones.reintento.*: el ConsumidorKafka ya esperó a que venciera.
    public async Task ManejarReintentoAsync(MensajeKafka mensaje, CancellationToken ct)
    {
        var pendiente = Leer<NotificacionPendienteV1>(mensaje.Valor);
        if (pendiente is null)
        {
            await InsistirAsync(() => enrutador.EnviarADlqAsync(mensaje.Clave ?? "", mensaje.Valor, "Mensaje ilegible", ct), ct);
            return;
        }

        await IntentarAsync(pendiente, ct);
    }

    private async Task IntentarAsync(NotificacionPendienteV1 pendiente, CancellationToken ct)
    {
        var canal = MapeadorNotificacion.CanalDesdeTexto(pendiente.Canal);
        var cambio = MapeadorNotificacion.ADominio(pendiente.Cambio);

        try
        {
            var resultado = await notificar.EjecutarAsync(cambio, canal, ct);
            logger.LogDebug("Notificación {Guia} v{Version} por {Canal}: {Resultado}", cambio.NumeroGuia, cambio.Version, canal, resultado);
            Medir(canal, Telemetria.Texto(resultado));
            if (resultado == ResultadoNotificacion.Enviada && pendiente.RecibidoEn is { } recibidoEn)
                Telemetria.LatenciaNotificacion.Record((DateTimeOffset.UtcNow - recibidoEn).TotalSeconds,
                    Telemetria.Etiqueta("canal", Telemetria.Texto(canal)));

            if (resultado == ResultadoNotificacion.SinDestino)
                await CanalAlternoODlqAsync(pendiente, canal, $"Sin destino para {canal}", ct);
        }
        catch (DestinoRechazadoException ex)
        {
            await CanalAlternoODlqAsync(pendiente, canal, ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ClasificadorErrores.EsTransitorio(ex))
        {
            if (pendiente.Intento < opciones.Reintentos.Count)
            {
                var siguiente = pendiente with { Intento = pendiente.Intento + 1 };
                logger.LogWarning("Notificación {Guia} v{Version} por {Canal} falló ({Motivo}); reintento {Intento} en {Espera}",
                    cambio.NumeroGuia, cambio.Version, canal, ex.Message, siguiente.Intento, opciones.Reintentos[pendiente.Intento].Espera);
                await InsistirAsync(() => enrutador.ProgramarReintentoAsync(siguiente, ct), ct);
                Medir(canal, "reintento");
            }
            else
            {
                await CanalAlternoODlqAsync(pendiente, canal, $"Reintentos agotados: {ex.Message}", ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error inesperado notificando {Guia} v{Version}", cambio.NumeroGuia, cambio.Version);
            await EnviarADlqAsync(pendiente, $"Error inesperado: {ex.GetType().Name}: {ex.Message}", ct);
        }
    }

    private async Task CanalAlternoODlqAsync(NotificacionPendienteV1 pendiente, CanalNotificacion canal, string motivo, CancellationToken ct)
    {
        if (PoliticaNotificacion.CanalAlterno(canal) is { } alterno)
        {
            logger.LogWarning("Notificación {Guia} v{Version}: {Motivo}; se intenta por {Alterno}",
                pendiente.Cambio.NumeroGuia, pendiente.Cambio.Version, motivo, alterno);
            await IntentarAsync(pendiente with { Canal = MapeadorNotificacion.CanalATexto(alterno), Intento = 0 }, ct);
            return;
        }

        await EnviarADlqAsync(pendiente, motivo, ct);
    }

    private async Task EnviarADlqAsync(NotificacionPendienteV1 pendiente, string motivo, CancellationToken ct)
    {
        logger.LogError("Notificación {Guia} v{Version} va a DLQ: {Motivo}", pendiente.Cambio.NumeroGuia, pendiente.Cambio.Version, motivo);
        await InsistirAsync(() => enrutador.EnviarADlqAsync(
            pendiente.Cambio.NumeroGuia, JsonSerializer.Serialize(pendiente, Json), motivo, ct), ct);
        Medir(MapeadorNotificacion.CanalDesdeTexto(pendiente.Canal), "fallida");
        Telemetria.MensajesDlq.Add(1, Telemetria.Etiqueta("origen", "notificador"));
    }

    private static void Medir(CanalNotificacion canal, string resultado) =>
        Telemetria.Notificaciones.Add(1,
            Telemetria.Etiqueta("canal", Telemetria.Texto(canal)),
            Telemetria.Etiqueta("resultado", resultado));

    // Publicar el reintento o la DLQ DEBE lograrse antes de avanzar el offset; si Kafka no responde, se insiste.
    private async Task InsistirAsync(Func<Task> publicar, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await publicar();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var espera = TimeSpan.FromMilliseconds(Math.Min(30_000, 200 * Math.Pow(2, Math.Min(intento - 1, 10))));
                logger.LogWarning(ex, "No se pudo publicar la etapa siguiente (intento {Intento}); se insiste en {Espera}", intento, espera);
                await Task.Delay(espera, ct);
            }
        }
    }

    private static T? Leer<T>(string? valor) where T : class
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(valor, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

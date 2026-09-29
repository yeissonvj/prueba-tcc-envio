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

/// <summary>
/// Política de enrutamiento del notificador. NUNCA bloquea: un proveedor caído no puede frenar
/// las notificaciones de las demás guías (al revés que el procesador, donde el orden obliga a esperar).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>éxito → listo.</item>
/// <item>proveedor no disponible → siguiente etapa de reintento (1 min → 10 min → 1 h).</item>
/// <item>reintentos agotados, destino rechazado o sin destino → canal alterno (con su propia escalera) y, si no hay, DLQ.</item>
/// <item>ilegible o error inesperado → DLQ.</item>
/// </list>
/// </remarks>
/// <param name="notificar">Caso de uso que envía una notificación.</param>
/// <param name="enrutador">Publica reintentos y DLQ.</param>
/// <param name="opciones">Escalera de reintentos.</param>
/// <param name="logger">Registro de reintentos, canal alterno y DLQ.</param>
public sealed class ManejadorNotificacion(
    NotificarCambioEstado notificar,
    IEnrutadorNotificaciones enrutador,
    OpcionesNotificador opciones,
    ILogger<ManejadorNotificacion> logger)
{
    /// <summary>Reglas de serialización web (camelCase).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maneja un mensaje de guias.estados.cambiados: primer intento por el canal principal (SMS).</summary>
    /// <param name="mensaje">Mensaje leído de Kafka.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la notificación se envió, se reprogramó o fue a la DLQ.</returns>
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

    /// <summary>Maneja un mensaje de notificaciones.reintento.*: el ConsumidorKafka ya esperó a que venciera.</summary>
    /// <param name="mensaje">Mensaje leído del tópico de reintento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la notificación se envió, se reprogramó o fue a la DLQ.</returns>
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

    /// <summary>
    /// Intenta enviar la notificación por su canal y decide la siguiente etapa según el resultado:
    /// listo, siguiente reintento, canal alterno o DLQ. Registra las métricas de notificación.
    /// </summary>
    /// <param name="pendiente">Notificación con su canal e intento actuales.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la notificación quedó resuelta o reprogramada.</returns>
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

    /// <summary>Pasa al canal alterno (empezando su propia escalera) o, si no hay más canales, a la DLQ.</summary>
    /// <param name="pendiente">Notificación que no se pudo enviar.</param>
    /// <param name="canal">Canal que falló.</param>
    /// <param name="motivo">Por qué falló.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando se intentó el canal alterno o se envió a la DLQ.</returns>
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

    /// <summary>Envía la notificación a notificaciones.dlq y registra las métricas de falla.</summary>
    /// <param name="pendiente">Notificación que no se pudo entregar.</param>
    /// <param name="motivo">Por qué se rechaza.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la DLQ confirmó.</returns>
    private async Task EnviarADlqAsync(NotificacionPendienteV1 pendiente, string motivo, CancellationToken ct)
    {
        logger.LogError("Notificación {Guia} v{Version} va a DLQ: {Motivo}", pendiente.Cambio.NumeroGuia, pendiente.Cambio.Version, motivo);
        await InsistirAsync(() => enrutador.EnviarADlqAsync(
            pendiente.Cambio.NumeroGuia, JsonSerializer.Serialize(pendiente, Json), motivo, ct), ct);
        Medir(MapeadorNotificacion.CanalDesdeTexto(pendiente.Canal), "fallida");
        Telemetria.MensajesDlq.Add(1, Telemetria.Etiqueta("origen", "notificador"));
    }

    /// <summary>Suma un intento de notificación a la métrica tcc.notificaciones.</summary>
    /// <param name="canal">Canal del intento.</param>
    /// <param name="resultado">Resultado en snake_case (enviada, reintento, fallida...).</param>
    private static void Medir(CanalNotificacion canal, string resultado) =>
        Telemetria.Notificaciones.Add(1,
            Telemetria.Etiqueta("canal", Telemetria.Texto(canal)),
            Telemetria.Etiqueta("resultado", resultado));

    // Publicar el reintento o la DLQ DEBE lograrse antes de avanzar el offset; si Kafka no responde, se insiste.
    /// <summary>Ejecuta una publicación y la repite con espera creciente (hasta 30 s) hasta que funcione.</summary>
    /// <param name="publicar">Publicación del reintento o de la DLQ.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la publicación se logró.</returns>
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

    /// <summary>Lee el JSON de un mensaje; si está vacío o mal formado devuelve <see langword="null"/>.</summary>
    /// <typeparam name="T">Contrato esperado.</typeparam>
    /// <param name="valor">Contenido del mensaje.</param>
    /// <returns>El contrato leído, o <see langword="null"/> si el mensaje es ilegible.</returns>
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

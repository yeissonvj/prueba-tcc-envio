using TccEventos.Aplicacion.CasosUso;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Infraestructura.Resiliencia;

namespace TccEventos.Procesador.Consumo;

/// <summary>
/// Política de procesamiento de un mensaje de guias.eventos.recibidos, sin conocer Kafka.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>ilegible (poison pill) → DLQ de inmediato; la partición sigue.</item>
/// <item>error transitorio → reintento BLOQUEANTE sin límite (no se salta nada: se respeta el orden).</item>
/// <item>error inesperado (p. ej. un bug) → pocos reintentos y DLQ, para no congelar la partición.</item>
/// </list>
/// Solo retorna cuando el mensaje quedó procesado o en la DLQ: entonces es seguro avanzar el offset.
/// </remarks>
/// <param name="procesarEvento">Caso de uso que aplica el evento.</param>
/// <param name="dlq">Destino de los mensajes rechazados.</param>
/// <param name="opciones">Intentos y esperas de los reintentos.</param>
/// <param name="logger">Registro de rechazos y reintentos.</param>
/// <param name="reloj">Reloj para medir la latencia; se reemplaza en las pruebas.</param>
public sealed class ManejadorMensajeRecibido(
    ProcesarEvento procesarEvento,
    IDestinoDlq dlq,
    OpcionesConsumidor opciones,
    ILogger<ManejadorMensajeRecibido> logger,
    TimeProvider? reloj = null)
{
    /// <summary>Reloj efectivo (el del sistema si no se indica otro).</summary>
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    /// <summary>
    /// Maneja un mensaje: lo lee, lo procesa con reintentos según el tipo de error y, si no hay forma, lo manda a la DLQ.
    /// Registra las métricas de eventos procesados y de latencia "estado visible".
    /// </summary>
    /// <param name="mensaje">Mensaje leído de Kafka.</param>
    /// <param name="ct">Se activa al detener el servicio: interrumpe los reintentos sin marcar el mensaje.</param>
    /// <returns>Una tarea que termina cuando el mensaje quedó procesado o en la DLQ.</returns>
    public async Task ManejarAsync(MensajeKafka mensaje, CancellationToken ct)
    {
        var (evento, motivo) = LectorMensajeRecibido.Leer(mensaje.Valor);
        if (evento is null)
        {
            logger.LogWarning("Mensaje ilegible en {Topico}[{Particion}]@{Offset} va a DLQ: {Motivo}",
                mensaje.Topico, mensaje.Particion, mensaje.Offset, motivo);
            await EnviarADlqAsync(new MensajeRechazado(mensaje, motivo!), ct);
            return;
        }

        var erroresInesperados = 0;
        for (var intento = 1; ; intento++)
        {
            try
            {
                var resultado = await procesarEvento.EjecutarAsync(evento, ct);
                logger.LogDebug("Evento {IdEvento} de la guía {NumeroGuia}: {Resultado}", evento.IdEvento, evento.NumeroGuia, resultado);

                Telemetria.EventosProcesados.Add(1, Telemetria.Etiqueta("resultado", Telemetria.Texto(resultado)));
                if (resultado == ResultadoProcesamiento.Aplicado && mensaje.Marca is { } recibidoEn)
                    Telemetria.LatenciaEstado.Record((_reloj.GetUtcNow() - recibidoEn).TotalSeconds);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!ClasificadorErrores.EsTransitorio(ex) && ++erroresInesperados >= opciones.IntentosErrorInesperado)
                {
                    logger.LogError(ex, "Evento {IdEvento} de la guía {NumeroGuia} va a DLQ tras {Intentos} errores inesperados",
                        evento.IdEvento, evento.NumeroGuia, erroresInesperados);
                    await EnviarADlqAsync(new MensajeRechazado(mensaje, $"Error no transitorio: {ex.GetType().Name}: {ex.Message}"), ct);
                    return;
                }

                var espera = Espera(intento);
                logger.LogWarning(ex, "Error procesando {IdEvento} de la guía {NumeroGuia} (intento {Intento}); reintento bloqueante en {Espera}",
                    evento.IdEvento, evento.NumeroGuia, intento, espera);
                await Task.Delay(espera, ct);
            }
        }
    }

    // Si la DLQ no confirma, tampoco se puede avanzar: se reintenta hasta que la guarde.
    /// <summary>Envía el mensaje a la DLQ e insiste hasta lograrlo (el offset no puede avanzar sin guardarlo).</summary>
    /// <param name="rechazado">Mensaje original con el motivo.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la DLQ confirmó.</returns>
    private async Task EnviarADlqAsync(MensajeRechazado rechazado, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await dlq.EnviarAsync(rechazado, ct);
                Telemetria.MensajesDlq.Add(1, Telemetria.Etiqueta("origen", "procesador"));
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo enviar a la DLQ (intento {Intento})", intento);
                await Task.Delay(Espera(intento), ct);
            }
        }
    }

    /// <summary>
    /// Espera antes del siguiente intento: exponencial con tope y un 20 % de variación aleatoria (jitter).
    /// </summary>
    /// <remarks>
    /// Con la variación, si muchas particiones fallan a la vez, no reintentan todas en el mismo instante
    /// contra la base que se está recuperando.
    /// </remarks>
    /// <param name="intento">Número del intento (1, 2, 3...).</param>
    /// <returns>Cuánto esperar: 200 ms, 400 ms, 800 ms... hasta el máximo configurado.</returns>
    private TimeSpan Espera(int intento)
    {
        var exponencial = opciones.EsperaMinimaMs * Math.Pow(2, Math.Min(intento - 1, 20));
        var conVariacion = exponencial * (1 + Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(Math.Min(opciones.EsperaMaximaMs, conVariacion));
    }
}

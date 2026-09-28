using TccEventos.Aplicacion.CasosUso;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Infraestructura.Resiliencia;

namespace TccEventos.Procesador.Consumo;

/// Política de procesamiento de un mensaje, sin conocer Kafka:
/// - ilegible (poison pill)            → DLQ de inmediato; la partición sigue.
/// - error transitorio                 → reintento BLOQUEANTE sin límite (no se salta nada: se respeta el orden).
/// - error inesperado (p. ej. un bug)  → pocos reintentos y DLQ, para no congelar la partición.
/// Solo retorna cuando el mensaje quedó procesado o en la DLQ: entonces es seguro avanzar el offset.
public sealed class ManejadorMensajeRecibido(
    ProcesarEvento procesarEvento,
    IDestinoDlq dlq,
    OpcionesConsumidor opciones,
    ILogger<ManejadorMensajeRecibido> logger,
    TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

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

    /// Exponencial con tope y un 20 % de variación aleatoria (jitter): si muchas particiones fallan a la vez,
    /// no reintentan todas en el mismo instante contra la base que se está recuperando.
    private TimeSpan Espera(int intento)
    {
        var exponencial = opciones.EsperaMinimaMs * Math.Pow(2, Math.Min(intento - 1, 20));
        var conVariacion = exponencial * (1 + Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(Math.Min(opciones.EsperaMaximaMs, conVariacion));
    }
}

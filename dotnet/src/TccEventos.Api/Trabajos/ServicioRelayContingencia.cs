using TccEventos.Aplicacion.CasosUso;

namespace TccEventos.Api.Trabajos;

/// <summary>
/// Servicio en segundo plano que cada pocos segundos vacía la contingencia hacia Kafka.
/// </summary>
/// <remarks>
/// Corre en todas las instancias de la API; el bloqueo SKIP LOCKED del almacén evita que dos instancias
/// reenvíen la misma fila.
/// </remarks>
/// <param name="reenviar">Caso de uso que reenvía los pendientes.</param>
/// <param name="logger">Registro de reenvíos y fallas.</param>
public sealed class ServicioRelayContingencia(
    ReenviarContingencia reenviar,
    ILogger<ServicioRelayContingencia> logger) : BackgroundService
{
    /// <summary>Eventos por lote.</summary>
    public const int TamanoLote = 500;

    /// <summary>Tiempo entre ciclos.</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    /// <summary>Bucle del servicio: en cada ciclo reenvía lotes hasta vaciar la contingencia.</summary>
    /// <param name="ct">Se activa cuando el host se detiene.</param>
    /// <returns>Una tarea que termina al detener el host.</returns>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(Intervalo);

        while (await temporizador.WaitForNextTickAsync(ct))
        {
            try
            {
                int reenviados;
                do
                {
                    reenviados = await reenviar.EjecutarAsync(TamanoLote, ct);
                    if (reenviados > 0)
                        logger.LogInformation("Relay: {Cantidad} eventos reenviados desde contingencia a Kafka", reenviados);
                }
                while (reenviados == TamanoLote); // lote lleno: probablemente hay más, seguir sin esperar
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // PostgreSQL caído o similar: se reintenta en el próximo ciclo; el servicio no muere.
                logger.LogWarning(ex, "Relay de contingencia falló; se reintenta en {Intervalo}", Intervalo);
            }
        }
    }
}

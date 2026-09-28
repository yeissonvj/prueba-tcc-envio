using TccEventos.Aplicacion.CasosUso;

namespace TccEventos.Api.Trabajos;

/// Cada pocos segundos vacía la contingencia hacia Kafka. Corre en todas las instancias de la API;
/// el bloqueo SKIP LOCKED del almacén evita que dos instancias reenvíen la misma fila.
public sealed class ServicioRelayContingencia(
    ReenviarContingencia reenviar,
    ILogger<ServicioRelayContingencia> logger) : BackgroundService
{
    public const int TamanoLote = 500;
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

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

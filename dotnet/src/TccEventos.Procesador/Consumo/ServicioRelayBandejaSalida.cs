using TccEventos.Infraestructura.Postgres;

namespace TccEventos.Procesador.Consumo;

/// Vacía la bandeja de salida hacia guias.estados.cambiados. Intervalo corto porque es parte
/// de la latencia "escaneo → cliente notificado". Si otra instancia tiene el candado, esta no hace nada.
public sealed class ServicioRelayBandejaSalida(
    RelayBandejaSalida relay,
    ILogger<ServicioRelayBandejaSalida> logger) : BackgroundService
{
    public const int TamanoLote = 500;
    public static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(Intervalo);

        while (await temporizador.WaitForNextTickAsync(ct))
        {
            try
            {
                int publicados;
                do
                {
                    publicados = await relay.PublicarPendientesAsync(TamanoLote, ct);
                    if (publicados > 0)
                        logger.LogDebug("Bandeja de salida: {Cantidad} cambios publicados", publicados);
                }
                while (publicados == TamanoLote);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Relay de bandeja de salida falló; se reintenta en el siguiente ciclo");
            }
        }
    }
}

using TccEventos.Infraestructura.Postgres;

namespace TccEventos.Procesador.Consumo;

/// <summary>
/// Servicio en segundo plano que vacía la bandeja de salida hacia guias.estados.cambiados.
/// </summary>
/// <remarks>
/// Intervalo corto porque es parte de la latencia "escaneo → cliente notificado".
/// Si otra instancia tiene el candado, esta no hace nada.
/// </remarks>
/// <param name="relay">Relay de la bandeja de salida.</param>
/// <param name="logger">Registro de publicaciones y fallas.</param>
public sealed class ServicioRelayBandejaSalida(
    RelayBandejaSalida relay,
    ILogger<ServicioRelayBandejaSalida> logger) : BackgroundService
{
    /// <summary>Cambios por lote.</summary>
    public const int TamanoLote = 500;

    /// <summary>Tiempo entre ciclos.</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(250);

    /// <summary>Bucle del servicio: en cada ciclo publica lotes hasta vaciar la bandeja.</summary>
    /// <param name="ct">Se activa cuando el host se detiene.</param>
    /// <returns>Una tarea que termina al detener el host.</returns>
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

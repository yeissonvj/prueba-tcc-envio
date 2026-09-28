using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Notificaciones;

/// Decorador + Circuit Breaker por proveedor: si el SMS está caído, se deja de insistir durante un tiempo
/// y los mensajes pasan directo a reintento en vez de esperar un timeout cada uno.
public sealed class ProveedorConCircuito : IProveedorNotificacion
{
    private readonly IProveedorNotificacion _interno;
    private readonly ResiliencePipeline _circuito;

    public ProveedorConCircuito(
        IProveedorNotificacion interno,
        OpcionesProveedores opciones,
        ILogger<ProveedorConCircuito> logger,
        TimeProvider? reloj = null)
    {
        _interno = interno;
        _circuito = new ResiliencePipelineBuilder { TimeProvider = reloj ?? TimeProvider.System }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<ProveedorNoDisponibleException>(),
                FailureRatio = 0.5,
                MinimumThroughput = opciones.CircuitoMinimoEnvios,
                SamplingDuration = TimeSpan.FromSeconds(opciones.CircuitoVentanaSegundos),
                BreakDuration = TimeSpan.FromSeconds(opciones.CircuitoSegundosAbierto),
                OnOpened = a =>
                {
                    logger.LogError("Circuito del proveedor de {Canal} ABIERTO por {Duracion}", interno.Canal, a.BreakDuration);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuito del proveedor de {Canal} CERRADO", interno.Canal);
                    return default;
                }
            })
            .Build();
    }

    public CanalNotificacion Canal => _interno.Canal;

    public async Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct)
    {
        try
        {
            await _circuito.ExecuteAsync(async token => await _interno.EnviarAsync(mensaje, token), ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new ProveedorNoDisponibleException($"Circuito del proveedor de {Canal} abierto; no se intentó enviar.", ex);
        }
    }
}

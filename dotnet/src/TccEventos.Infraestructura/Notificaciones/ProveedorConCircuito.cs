using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Notificaciones;

/// <summary>
/// Decorador + Circuit Breaker por proveedor: si el SMS está caído, se deja de insistir durante un tiempo
/// y los mensajes pasan directo a reintento en vez de esperar un timeout cada uno.
/// </summary>
public sealed class ProveedorConCircuito : IProveedorNotificacion
{
    /// <summary>Proveedor real que se protege.</summary>
    private readonly IProveedorNotificacion _interno;

    /// <summary>Circuito de Polly de este proveedor.</summary>
    private readonly ResiliencePipeline _circuito;

    /// <summary>Crea el decorador con los umbrales configurados.</summary>
    /// <param name="interno">Proveedor real.</param>
    /// <param name="opciones">Umbrales del circuito.</param>
    /// <param name="logger">Registro de aperturas y cierres del circuito.</param>
    /// <param name="reloj">Reloj del circuito; se reemplaza en las pruebas.</param>
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

    /// <summary>Canal del proveedor que se protege.</summary>
    public CanalNotificacion Canal => _interno.Canal;

    /// <summary>Envía a través del circuito.</summary>
    /// <param name="mensaje">Mensaje a enviar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el proveedor aceptó el mensaje.</returns>
    /// <exception cref="ProveedorNoDisponibleException">El proveedor falló o el circuito está abierto.</exception>
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

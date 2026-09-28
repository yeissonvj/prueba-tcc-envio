using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Kafka;

/// Patrón Decorador + Circuit Breaker: si el broker falla de forma sostenida, durante un tiempo
/// se falla al instante en vez de esperar el timeout de entrega en cada petición.
public sealed class PublicadorConCircuito : IPublicadorEventos
{
    private readonly IPublicadorEventos _interno;
    private readonly ResiliencePipeline _circuito;

    public PublicadorConCircuito(
        IPublicadorEventos interno,
        OpcionesKafka opciones,
        ILogger<PublicadorConCircuito> logger,
        TimeProvider? reloj = null)
    {
        _interno = interno;
        _circuito = new ResiliencePipelineBuilder { TimeProvider = reloj ?? TimeProvider.System }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<PublicacionFallidaException>(),
                FailureRatio = 0.5,
                MinimumThroughput = opciones.CircuitoMinimoEnvios,
                SamplingDuration = TimeSpan.FromSeconds(opciones.CircuitoVentanaSegundos),
                BreakDuration = TimeSpan.FromSeconds(opciones.CircuitoSegundosAbierto),
                OnOpened = a =>
                {
                    logger.LogError(a.Outcome.Exception, "Circuito hacia Kafka ABIERTO por {Duracion}: los eventos van a contingencia", a.BreakDuration);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogWarning("Circuito hacia Kafka SEMIABIERTO: probando con el siguiente envío");
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuito hacia Kafka CERRADO: publicación normal");
                    return default;
                }
            })
            .Build();
    }

    public async Task PublicarAsync(EventoGuia evento, CancellationToken ct)
    {
        try
        {
            await _circuito.ExecuteAsync(async token => await _interno.PublicarAsync(evento, token), ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new PublicacionFallidaException("Circuito hacia Kafka abierto; no se intentó publicar.", ex);
        }
    }
}

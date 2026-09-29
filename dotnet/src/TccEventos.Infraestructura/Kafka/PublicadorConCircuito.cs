using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Patrón Decorador + Circuit Breaker: si el broker falla de forma sostenida, durante un tiempo
/// se falla al instante en vez de esperar el timeout de entrega en cada petición.
/// </summary>
/// <remarks>
/// Se abre si en la ventana falla al menos la mitad de un mínimo de envíos; mientras está abierto,
/// los eventos van directo a la contingencia.
/// </remarks>
public sealed class PublicadorConCircuito : IPublicadorEventos
{
    /// <summary>Publicador real que se protege.</summary>
    private readonly IPublicadorEventos _interno;

    /// <summary>Circuito de Polly.</summary>
    private readonly ResiliencePipeline _circuito;

    /// <summary>Crea el decorador con los umbrales configurados.</summary>
    /// <param name="interno">Publicador real (Kafka).</param>
    /// <param name="opciones">Umbrales del circuito.</param>
    /// <param name="logger">Registro de aperturas y cierres del circuito.</param>
    /// <param name="reloj">Reloj del circuito; se reemplaza en las pruebas.</param>
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

    /// <summary>Publica a través del circuito.</summary>
    /// <param name="evento">Evento a publicar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando Kafka confirmó.</returns>
    /// <exception cref="PublicacionFallidaException">Kafka falló o el circuito está abierto.</exception>
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

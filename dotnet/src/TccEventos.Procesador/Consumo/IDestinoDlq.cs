using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.Consumo;

/// <summary>
/// Destino de los mensajes que el procesador no puede procesar (Dead Letter Queue).
/// </summary>
/// <remarks>Solo termina con éxito si el mensaje rechazado quedó guardado: si no, el offset no puede avanzar.</remarks>
public interface IDestinoDlq
{
    /// <summary>Guarda un mensaje rechazado en la DLQ.</summary>
    /// <param name="rechazado">Mensaje original con el motivo.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando quedó guardado.</returns>
    Task EnviarAsync(MensajeRechazado rechazado, CancellationToken ct);
}

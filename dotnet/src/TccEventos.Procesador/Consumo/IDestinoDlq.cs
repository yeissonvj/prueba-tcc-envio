using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.Consumo;

/// Solo termina con éxito si el mensaje rechazado quedó guardado: si no, el offset no puede avanzar.
public interface IDestinoDlq
{
    Task EnviarAsync(MensajeRechazado rechazado, CancellationToken ct);
}

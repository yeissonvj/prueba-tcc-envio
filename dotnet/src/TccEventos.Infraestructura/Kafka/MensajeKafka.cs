namespace TccEventos.Infraestructura.Kafka;

/// Lo que un manejador necesita de un mensaje, sin depender de los tipos de Confluent.Kafka:
/// así las políticas de reintento y DLQ se prueban sin broker.
public record MensajeKafka(
    string Topico,
    int Particion,
    long Offset,
    string? Clave,
    string? Valor,
    IReadOnlyDictionary<string, string>? Encabezados = null,
    DateTimeOffset? Marca = null)   // cuándo Kafka recibió el mensaje: base para medir latencias
{
    public string? Encabezado(string nombre) => Encabezados?.GetValueOrDefault(nombre);
}

public record MensajeRechazado(MensajeKafka Original, string Motivo);

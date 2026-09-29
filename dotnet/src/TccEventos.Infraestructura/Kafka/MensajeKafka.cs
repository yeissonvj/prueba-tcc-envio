namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Lo que un manejador necesita de un mensaje, sin depender de los tipos de Confluent.Kafka:
/// así las políticas de reintento y DLQ se prueban sin broker.
/// </summary>
/// <param name="Topico">Tópico de donde se leyó.</param>
/// <param name="Particion">Partición de donde se leyó.</param>
/// <param name="Offset">Posición del mensaje en la partición.</param>
/// <param name="Clave">Clave del mensaje (el número de guía).</param>
/// <param name="Valor">Contenido del mensaje (JSON).</param>
/// <param name="Encabezados">Encabezados del mensaje (traza, contrato, vencimiento...).</param>
/// <param name="Marca">Cuándo Kafka recibió el mensaje: base para medir latencias.</param>
public record MensajeKafka(
    string Topico,
    int Particion,
    long Offset,
    string? Clave,
    string? Valor,
    IReadOnlyDictionary<string, string>? Encabezados = null,
    DateTimeOffset? Marca = null)   // cuándo Kafka recibió el mensaje: base para medir latencias
{
    /// <summary>Lee un encabezado del mensaje.</summary>
    /// <param name="nombre">Nombre del encabezado.</param>
    /// <returns>Su valor, o <see langword="null"/> si no viene.</returns>
    public string? Encabezado(string nombre) => Encabezados?.GetValueOrDefault(nombre);
}

/// <summary>Un mensaje que no se pudo procesar y va a la DLQ, con el motivo.</summary>
/// <param name="Original">Mensaje original, intacto.</param>
/// <param name="Motivo">Por qué se rechazó.</param>
public record MensajeRechazado(MensajeKafka Original, string Motivo);

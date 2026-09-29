using Confluent.Kafka;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Api.Salud;

/// <summary>
/// Sonda de Kafka: está disponible solo si puede aceptar escrituras con acks=all, es decir, si cada partición
/// del tópico tiene al menos min.insync.replicas réplicas sincronizadas. Que "responda" no basta.
/// </summary>
/// <param name="opciones">Opciones de Kafka (conexión, tópico y réplicas mínimas).</param>
public sealed class SondaKafka(OpcionesKafka opciones) : ISonda, IDisposable
{
    /// <summary>Tiempo máximo de la consulta de metadatos.</summary>
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    /// <summary>Cliente de administración, creado solo cuando se usa por primera vez.</summary>
    private readonly Lazy<IAdminClient> _admin = new(() =>
        new AdminClientBuilder(opciones.ConfigurarConexion(new AdminClientConfig())).Build());

    /// <summary>Consulta los metadatos del tópico y revisa las réplicas sincronizadas de cada partición.</summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si Kafka puede escribir con acks=all.</returns>
    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var metadatos = _admin.Value.GetMetadata(opciones.TopicoEventosRecibidos, Limite);
            var topico = metadatos.Topics.SingleOrDefault(t => t.Topic == opciones.TopicoEventosRecibidos);

            return topico is { Error.IsError: false, Partitions.Count: > 0 }
                && topico.Partitions.All(p => p.InSyncReplicas.Length >= opciones.ReplicasMinimasSincronizadas);
        }
        catch (KafkaException)
        {
            return false;
        }
    }, ct);

    /// <summary>Libera el cliente de administración si se llegó a crear.</summary>
    public void Dispose()
    {
        if (_admin.IsValueCreated)
            _admin.Value.Dispose();
    }
}

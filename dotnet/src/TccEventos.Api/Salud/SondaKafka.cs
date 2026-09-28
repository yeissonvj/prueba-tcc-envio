using Confluent.Kafka;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Api.Salud;

/// Kafka está disponible solo si puede aceptar escrituras con acks=all: cada partición del tópico
/// debe tener al menos min.insync.replicas réplicas sincronizadas. Que "responda" no basta.
public sealed class SondaKafka(OpcionesKafka opciones) : ISonda, IDisposable
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    private readonly Lazy<IAdminClient> _admin = new(() =>
        new AdminClientBuilder(opciones.ConfigurarConexion(new AdminClientConfig())).Build());

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

    public void Dispose()
    {
        if (_admin.IsValueCreated)
            _admin.Value.Dispose();
    }
}

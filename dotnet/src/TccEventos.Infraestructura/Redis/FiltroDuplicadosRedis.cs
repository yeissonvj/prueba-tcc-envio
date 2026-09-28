using StackExchange.Redis;

using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Infraestructura.Redis;

/// Adaptador: implementa el puerto IFiltroDuplicados con Redis. Solo habla con Redis;
/// la tolerancia a fallas la agrega el decorador FiltroDuplicadosTolerante.
public sealed class FiltroDuplicadosRedis(IConnectionMultiplexer redis, OpcionesRedis opciones) : IFiltroDuplicados
{
    public Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        redis.GetDatabase().KeyExistsAsync(Clave(idEvento));

    public Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Clave(idEvento), "1", opciones.Vigencia);

    private RedisKey Clave(Guid idEvento) => $"{opciones.PrefijoClave}{idEvento:N}";
}
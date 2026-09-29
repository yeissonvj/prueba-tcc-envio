using StackExchange.Redis;

using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Infraestructura.Redis;

/// <summary>
/// Adaptador: implementa el puerto <see cref="IFiltroDuplicados"/> con Redis.
/// </summary>
/// <remarks>
/// Solo habla con Redis; la tolerancia a fallas la agrega el decorador FiltroDuplicadosTolerante.
/// </remarks>
/// <param name="redis">Conexión compartida a Redis.</param>
/// <param name="opciones">Prefijo de las claves y vigencia.</param>
public sealed class FiltroDuplicadosRedis(IConnectionMultiplexer redis, OpcionesRedis opciones) : IFiltroDuplicados
{
    /// <summary>Indica si la clave del evento existe en Redis.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación (Redis no lo usa).</param>
    /// <returns><see langword="true"/> si el evento se recibió dentro de la vigencia.</returns>
    public Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        redis.GetDatabase().KeyExistsAsync(Clave(idEvento));

    /// <summary>Guarda la clave del evento con la vigencia configurada (72 h por defecto).</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación (Redis no lo usa).</param>
    /// <returns>Una tarea que termina cuando Redis guardó la clave.</returns>
    public Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Clave(idEvento), "1", opciones.Vigencia);

    /// <summary>Arma la clave de Redis del evento: prefijo + id sin guiones.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <returns>Por ejemplo <c>tcc:eventos:recibidos:0199a1b27c3d...</c>.</returns>
    private RedisKey Clave(Guid idEvento) => $"{opciones.PrefijoClave}{idEvento:N}";
}

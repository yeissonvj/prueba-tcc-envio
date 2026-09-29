using StackExchange.Redis;

namespace TccEventos.Api.Salud;

/// <summary>Sonda del filtro de duplicados: un PING a Redis.</summary>
/// <param name="redis">Conexión compartida a Redis.</param>
public sealed class SondaRedis(IConnectionMultiplexer redis) : ISonda
{
    /// <summary>Envía un PING a Redis.</summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si Redis respondió.</returns>
    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        try
        {
            // El límite lo impone asyncTimeout de la conexión (250 ms).
            await redis.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

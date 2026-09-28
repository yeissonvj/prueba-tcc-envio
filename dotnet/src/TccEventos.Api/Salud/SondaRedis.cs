using StackExchange.Redis;

namespace TccEventos.Api.Salud;

public sealed class SondaRedis(IConnectionMultiplexer redis) : ISonda
{
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

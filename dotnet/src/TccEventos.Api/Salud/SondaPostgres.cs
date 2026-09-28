using Npgsql;

namespace TccEventos.Api.Salud;

/// Consulta la tabla de contingencia: confirma a la vez conexión, credenciales y que la migración se aplicó.
public sealed class SondaPostgres(NpgsqlDataSource baseDatos) : ISonda
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(Limite);
        try
        {
            await using var comando = baseDatos.CreateCommand("SELECT EXISTS (SELECT 1 FROM contingencia_eventos)");
            await comando.ExecuteScalarAsync(limite.Token);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

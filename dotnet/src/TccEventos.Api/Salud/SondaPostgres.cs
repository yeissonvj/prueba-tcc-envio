using Npgsql;

namespace TccEventos.Api.Salud;

/// <summary>
/// Sonda de la contingencia: consulta la tabla contingencia_eventos, lo que confirma a la vez
/// la conexión, las credenciales y que la migración se aplicó.
/// </summary>
/// <param name="baseDatos">Fuente de conexiones a PostgreSQL.</param>
public sealed class SondaPostgres(NpgsqlDataSource baseDatos) : ISonda
{
    /// <summary>Tiempo máximo de la consulta.</summary>
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    /// <summary>Ejecuta una consulta liviana con límite de tiempo.</summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si PostgreSQL respondió a tiempo.</returns>
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

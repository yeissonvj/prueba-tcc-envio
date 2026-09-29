using Npgsql;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Infraestructura.Postgres;

/// <summary>
/// Adaptador de <see cref="IRegistroNotificaciones"/> sobre la tabla notificaciones_enviadas (migración V3).
/// </summary>
/// <param name="baseDatos">Fuente de conexiones a PostgreSQL.</param>
public sealed class RegistroNotificacionesPostgres(NpgsqlDataSource baseDatos) : IRegistroNotificaciones
{
    /// <summary>Indica si la notificación ya se envió o si es obsoleta, en una sola consulta.</summary>
    /// <param name="cambio">Cambio que se quiere notificar.</param>
    /// <param name="canal">Canal por el que se enviaría.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>YaEnviada, Obsoleta o Pendiente.</returns>
    public async Task<EstadoNotificacion> ConsultarAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct)
    {
        // Las dos preguntas en una consulta: ¿esta ya salió? ¿salió algo más reciente de la guía?
        await using var comando = baseDatos.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM notificaciones_enviadas WHERE clave = $1),
                   EXISTS (SELECT 1 FROM notificaciones_enviadas WHERE numero_guia = $2 AND version > $3)
            """);
        comando.Parameters.Add(new NpgsqlParameter { Value = PoliticaNotificacion.ClaveIdempotencia(cambio, canal) });
        comando.Parameters.Add(new NpgsqlParameter { Value = cambio.NumeroGuia });
        comando.Parameters.Add(new NpgsqlParameter { Value = cambio.Version });

        await using var lector = await comando.ExecuteReaderAsync(ct);
        await lector.ReadAsync(ct);

        return lector.GetBoolean(0) ? EstadoNotificacion.YaEnviada
             : lector.GetBoolean(1) ? EstadoNotificacion.Obsoleta
             : EstadoNotificacion.Pendiente;
    }

    /// <summary>Registra el envío; si la clave ya existía, no hace nada (idempotente).</summary>
    /// <param name="cambio">Cambio notificado.</param>
    /// <param name="canal">Canal usado.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando quedó registrado.</returns>
    public async Task RegistrarEnvioAsync(CambioEstadoGuia cambio, CanalNotificacion canal, CancellationToken ct)
    {
        await using var comando = baseDatos.CreateCommand("""
            INSERT INTO notificaciones_enviadas (clave, numero_guia, version, canal)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (clave) DO NOTHING
            """);
        comando.Parameters.Add(new NpgsqlParameter { Value = PoliticaNotificacion.ClaveIdempotencia(cambio, canal) });
        comando.Parameters.Add(new NpgsqlParameter { Value = cambio.NumeroGuia });
        comando.Parameters.Add(new NpgsqlParameter { Value = cambio.Version });
        comando.Parameters.Add(new NpgsqlParameter { Value = MapeadorNotificacion.CanalATexto(canal) });

        await comando.ExecuteNonQueryAsync(ct);
    }
}

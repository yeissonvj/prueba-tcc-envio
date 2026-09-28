using Npgsql;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Infraestructura.Postgres;

/// Adaptador de IRegistroNotificaciones sobre la tabla notificaciones_enviadas (migración V3).
public sealed class RegistroNotificacionesPostgres(NpgsqlDataSource baseDatos) : IRegistroNotificaciones
{
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

using Npgsql;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Infraestructura.Postgres;

/// Publica la bandeja de salida (outbox) en guias.estados.cambiados y borra lo publicado.
/// Un solo relay activo a la vez (advisory lock): si varias instancias publicaran en paralelo,
/// dos cambios de la misma guía podrían salir desordenados.
public sealed class RelayBandejaSalida(NpgsqlDataSource baseDatos, ProductorKafka productor, OpcionesKafka opciones)
{
    // Identificador arbitrario y fijo del candado "relay de bandeja de salida".
    private const long ClaveCandado = 7_100_600_001;

    /// Devuelve cuántos mensajes publicó; 0 si no había pendientes u otra instancia tiene el candado.
    public async Task<int> PublicarPendientesAsync(int maximo, CancellationToken ct)
    {
        await using var conexion = await baseDatos.OpenConnectionAsync(ct);
        await using var transaccion = await conexion.BeginTransactionAsync(ct);

        // Candado de transacción: se libera solo al terminar (commit, rollback o caída de la conexión).
        await using (var candado = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock($1)", conexion, transaccion))
        {
            candado.Parameters.Add(new NpgsqlParameter { Value = ClaveCandado });
            if (!(bool)(await candado.ExecuteScalarAsync(ct))!)
                return 0;
        }

        var pendientes = new List<(long Id, string NumeroGuia, string Tipo, string Carga, string? Traza)>();
        await using (var leer = new NpgsqlCommand(
            "SELECT id, numero_guia, tipo, carga::text, contexto_traza FROM bandeja_salida ORDER BY id LIMIT $1", conexion, transaccion))
        {
            leer.Parameters.Add(new NpgsqlParameter { Value = maximo });
            await using var lector = await leer.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
                pendientes.Add((lector.GetInt64(0), lector.GetString(1), lector.GetString(2), lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4)));
        }

        if (pendientes.Count == 0)
            return 0;

        // Se inician en orden de id y se esperan juntos: el productor idempotente conserva el orden por
        // partición, y si un mensaje de una partición falla, los siguientes de esa partición también fallan.
        var envios = pendientes
            .Select(p => (p.Id, Envio: productor.PublicarAsync(
                opciones.TopicoEstadosCambiados, p.NumeroGuia, p.Carga, Encabezados(p.Tipo, p.Traza), ct)))
            .ToList();

        var publicados = new List<long>(envios.Count);
        foreach (var (id, envio) in envios)
        {
            try
            {
                await envio;
                publicados.Add(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Queda en la bandeja para el siguiente ciclo.
            }
        }

        await using (var borrar = new NpgsqlCommand("DELETE FROM bandeja_salida WHERE id = ANY($1)", conexion, transaccion))
        {
            borrar.Parameters.Add(new NpgsqlParameter { Value = publicados.ToArray() });
            await borrar.ExecuteNonQueryAsync(ct);
        }

        await transaccion.CommitAsync(ct);
        return publicados.Count;
    }

    // El contexto de traza guardado con el cambio reengancha la publicación a la traza del evento original.
    private static Dictionary<string, string> Encabezados(string tipo, string? traza)
    {
        var encabezados = new Dictionary<string, string> { ["contrato"] = tipo };
        if (traza is not null)
            encabezados[ProductorKafka.EncabezadoTraza] = traza;
        return encabezados;
    }
}

using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;
using TccEventos.Infraestructura.Observabilidad;

namespace TccEventos.Infraestructura.Postgres;

/// Adaptador: contingencia en PostgreSQL (tabla contingencia_eventos, migración V1).
/// SQL parametrizado siempre: los datos del evento nunca se concatenan en la consulta.
public sealed class AlmacenContingenciaPostgres(NpgsqlDataSource baseDatos) : IAlmacenContingencia
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task GuardarAsync(EventoGuia evento, CancellationToken ct)
    {
        await using var comando = baseDatos.CreateCommand("""
            INSERT INTO contingencia_eventos (id_evento, numero_guia, carga)
            VALUES ($1, $2, $3)
            ON CONFLICT (id_evento) DO NOTHING
            """);
        comando.Parameters.Add(new NpgsqlParameter { Value = evento.IdEvento });
        comando.Parameters.Add(new NpgsqlParameter { Value = evento.NumeroGuia });
        comando.Parameters.Add(new NpgsqlParameter
        {
            Value = JsonSerializer.Serialize(MapeadorEventoGuia.AContrato(evento), Json),
            NpgsqlDbType = NpgsqlDbType.Jsonb
        });

        await comando.ExecuteNonQueryAsync(ct);
        Telemetria.EventosEnContingencia.Add(1);
    }

    public async Task<int> ReenviarPendientesAsync(
        Func<EventoGuia, CancellationToken, Task> publicar, int maximo, CancellationToken ct)
    {
        await using var conexion = await baseDatos.OpenConnectionAsync(ct);
        await using var transaccion = await conexion.BeginTransactionAsync(ct);

        // SKIP LOCKED: varias instancias pueden vaciar la tabla a la vez sin tomar las mismas filas.
        var pendientes = new List<(Guid Id, EventoGuia Evento)>();
        await using (var leer = new NpgsqlCommand("""
            SELECT id_evento, carga
            FROM contingencia_eventos
            ORDER BY recibido_en, id_evento
            LIMIT $1
            FOR UPDATE SKIP LOCKED
            """, conexion, transaccion))
        {
            leer.Parameters.Add(new NpgsqlParameter { Value = maximo });
            await using var lector = await leer.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                var contrato = JsonSerializer.Deserialize<EventoGuiaV1>(lector.GetString(1), Json)!;
                pendientes.Add((lector.GetGuid(0), MapeadorEventoGuia.ADominio(contrato)));
            }
        }

        if (pendientes.Count == 0)
            return 0;

        // Se inician en orden y se esperan juntos: el productor idempotente conserva el orden
        // por partición y el lote sale en una fracción del tiempo que tomaría uno por uno.
        var envios = pendientes.Select(p => (p.Id, Envio: Iniciar(publicar, p.Evento, ct))).ToList();
        var publicados = new List<Guid>(envios.Count);
        foreach (var (id, envio) in envios)
        {
            try
            {
                await envio;
                publicados.Add(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Queda en la tabla para el siguiente ciclo.
            }
        }

        await using (var borrar = new NpgsqlCommand(
            "DELETE FROM contingencia_eventos WHERE id_evento = ANY($1)", conexion, transaccion))
        {
            borrar.Parameters.Add(new NpgsqlParameter { Value = publicados.ToArray() });
            await borrar.ExecuteNonQueryAsync(ct);
        }

        await transaccion.CommitAsync(ct);
        return publicados.Count;
    }

    // Un publicador que lanza de forma síncrona (en vez de devolver una Task fallida) abortaría el lote
    // antes del DELETE, y los ya publicados se reenviarían en el siguiente ciclo.
    private static Task Iniciar(Func<EventoGuia, CancellationToken, Task> publicar, EventoGuia evento, CancellationToken ct)
    {
        try
        {
            return publicar(evento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }
}

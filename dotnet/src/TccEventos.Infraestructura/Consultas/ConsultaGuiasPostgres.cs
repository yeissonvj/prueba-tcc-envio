using Npgsql;
using TccEventos.Contratos.V1;

namespace TccEventos.Infraestructura.Consultas;

public sealed class ConsultaGuiasPostgres(NpgsqlDataSource baseDatos) : IConsultaGuias
{
    public const int MaximoHistorial = 100;

    public async Task<GuiaV1?> ObtenerAsync(string numeroGuia, CancellationToken ct)
    {
        // Las dos lecturas en un solo viaje a la base.
        await using var lote = baseDatos.CreateBatch();

        var guia = new NpgsqlBatchCommand(
            "SELECT estado_actual, ultimo_evento_en, version FROM guias WHERE numero_guia = $1");
        guia.Parameters.Add(new NpgsqlParameter { Value = numeroGuia });
        lote.BatchCommands.Add(guia);

        var historial = new NpgsqlBatchCommand("""
            SELECT id_evento, estado, ocurrido_en, origen, novedad, resultado
            FROM historial_eventos
            WHERE numero_guia = $1
            ORDER BY ocurrido_en DESC, procesado_en DESC
            LIMIT $2
            """);
        historial.Parameters.Add(new NpgsqlParameter { Value = numeroGuia });
        historial.Parameters.Add(new NpgsqlParameter { Value = MaximoHistorial });
        lote.BatchCommands.Add(historial);

        await using var lector = await lote.ExecuteReaderAsync(ct);

        if (!await lector.ReadAsync(ct))
            return null;
        var (estado, ultimoEventoEn, version) =
            (lector.GetString(0), lector.GetFieldValue<DateTimeOffset>(1), lector.GetInt64(2));

        await lector.NextResultAsync(ct);
        var eventos = new List<EventoHistorialV1>();
        while (await lector.ReadAsync(ct))
        {
            eventos.Add(new EventoHistorialV1(
                lector.GetGuid(0),
                lector.GetString(1),
                lector.GetFieldValue<DateTimeOffset>(2),
                lector.GetString(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.GetString(5)));
        }

        return new GuiaV1(numeroGuia, estado, ultimoEventoEn, version, eventos);
    }
}

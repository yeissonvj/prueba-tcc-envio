using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Infraestructura.Postgres;

/// <summary>
/// Adaptador de <see cref="IRepositorioGuias"/> sobre las tablas de la migración V2 (patrones Repositorio,
/// Inbox y Transactional Outbox).
/// </summary>
/// <remarks>
/// Historial (inbox) + estado + bandeja de salida en UNA transacción y un solo viaje a la base (NpgsqlBatch).
/// Las fechas se escriben en UTC (Npgsql lo exige para timestamptz); el offset original se conserva en el JSON del contrato.
/// </remarks>
/// <param name="baseDatos">Fuente de conexiones a PostgreSQL.</param>
public sealed class RepositorioGuiasPostgres(NpgsqlDataSource baseDatos) : IRepositorioGuias
{
    /// <summary>Reglas de serialización web (camelCase) para el cambio que va a la bandeja de salida.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Consulta el inbox: indica si el evento ya está en historial_eventos.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si el evento ya se procesó.</returns>
    public async Task<bool> ExisteEventoAsync(Guid idEvento, CancellationToken ct)
    {
        await using var comando = baseDatos.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM historial_eventos WHERE id_evento = $1)");
        comando.Parameters.Add(new NpgsqlParameter { Value = idEvento });

        return (bool)(await comando.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Carga la guía desde la tabla guias y la reconstruye como objeto del dominio.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>La guía, o <see langword="null"/> si no existe.</returns>
    public async Task<Guia?> ObtenerAsync(string numeroGuia, CancellationToken ct)
    {
        await using var comando = baseDatos.CreateCommand(
            "SELECT estado_actual, ultimo_evento_en, version FROM guias WHERE numero_guia = $1");
        comando.Parameters.Add(new NpgsqlParameter { Value = numeroGuia });

        await using var lector = await comando.ExecuteReaderAsync(ct);
        if (!await lector.ReadAsync(ct))
            return null;

        return Guia.Reconstruir(
            numeroGuia,
            MapeadorEventoGuia.EstadoDesdeTexto(lector.GetString(0)),
            lector.GetFieldValue<DateTimeOffset>(1),
            lector.GetInt64(2));
    }

    /// <summary>
    /// Guarda en una sola transacción: la línea del historial (inbox) y, si el evento se aplicó,
    /// el nuevo estado (con control de versión) y el cambio en la bandeja de salida.
    /// </summary>
    /// <param name="guia">Guía con el estado ya actualizado en memoria.</param>
    /// <param name="evento">Evento procesado.</param>
    /// <param name="resultado">Resultado de aplicar el evento.</param>
    /// <param name="estadoAnterior">Estado previo; <see langword="null"/> si el evento creó la guía (INSERT en vez de UPDATE).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la transacción se confirmó.</returns>
    /// <exception cref="ConflictoConcurrenciaException">
    /// Otra instancia guardó el evento o cambió la guía primero; se hace rollback completo.
    /// </exception>
    public async Task GuardarAsync(
        Guia guia, EventoGuia evento, ResultadoAplicacion resultado, EstadoGuia? estadoAnterior, CancellationToken ct)
    {
        await using var conexion = await baseDatos.OpenConnectionAsync(ct);
        await using var transaccion = await conexion.BeginTransactionAsync(ct);
        await using var lote = new NpgsqlBatch(conexion, transaccion);

        // ON CONFLICT DO NOTHING: si otra instancia ya lo guardó, afecta 0 filas (conflicto, no excepción).
        lote.BatchCommands.Add(Comando("""
            INSERT INTO historial_eventos (id_evento, numero_guia, estado, ocurrido_en, origen, novedad, resultado)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (id_evento) DO NOTHING
            """,
            evento.IdEvento, evento.NumeroGuia, MapeadorEventoGuia.EstadoATexto(evento.Estado), evento.OcurridoEn.ToUniversalTime(),
            evento.Origen, (object?)evento.Novedad ?? DBNull.Value, MapeadorEventoGuia.ResultadoATexto(resultado)));

        if (resultado == ResultadoAplicacion.Aplicado)
        {
            var estado = MapeadorEventoGuia.EstadoATexto(guia.EstadoActual);

            lote.BatchCommands.Add(estadoAnterior is null
                ? Comando("""
                    INSERT INTO guias (numero_guia, estado_actual, ultimo_evento_en, version)
                    VALUES ($1, $2, $3, $4)
                    ON CONFLICT (numero_guia) DO NOTHING
                    """,
                    guia.NumeroGuia, estado, guia.UltimoEventoEn.ToUniversalTime(), guia.Version)
                : Comando("""
                    UPDATE guias
                    SET estado_actual = $2, ultimo_evento_en = $3, version = $4, actualizado_en = now()
                    WHERE numero_guia = $1 AND version = $5
                    """,
                    guia.NumeroGuia, estado, guia.UltimoEventoEn.ToUniversalTime(), guia.Version, guia.Version - 1));

            var cambio = MapeadorEventoGuia.ACambioEstado(guia, evento, estadoAnterior);
            var carga = new NpgsqlBatchCommand(
                "INSERT INTO bandeja_salida (numero_guia, tipo, carga, contexto_traza) VALUES ($1, $2, $3, $4)");
            carga.Parameters.Add(new NpgsqlParameter { Value = guia.NumeroGuia });
            carga.Parameters.Add(new NpgsqlParameter { Value = EstadoGuiaCambiadoV1.Tipo });
            carga.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(cambio, Json), NpgsqlDbType = NpgsqlDbType.Jsonb });
            carga.Parameters.Add(new NpgsqlParameter { Value = (object?)Activity.Current?.Id ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Varchar });
            lote.BatchCommands.Add(carga);
        }

        await lote.ExecuteNonQueryAsync(ct);

        // Historial y guía deben afectar exactamente 1 fila; si no, otra instancia se adelantó.
        var historialGuardado = lote.BatchCommands[0].RecordsAffected == 1;
        var guiaGuardada = resultado != ResultadoAplicacion.Aplicado || lote.BatchCommands[1].RecordsAffected == 1;
        if (!historialGuardado || !guiaGuardada)
        {
            await transaccion.RollbackAsync(ct);
            throw new ConflictoConcurrenciaException(guia.NumeroGuia);
        }

        await transaccion.CommitAsync(ct);
    }

    /// <summary>Crea una sentencia del lote con parámetros posicionales ($1, $2...).</summary>
    /// <param name="sql">Sentencia SQL parametrizada.</param>
    /// <param name="valores">Valores de los parámetros, en orden.</param>
    /// <returns>La sentencia lista para agregar al lote.</returns>
    private static NpgsqlBatchCommand Comando(string sql, params object[] valores)
    {
        var comando = new NpgsqlBatchCommand(sql);
        foreach (var valor in valores)
            comando.Parameters.Add(new NpgsqlParameter { Value = valor });
        return comando;
    }
}

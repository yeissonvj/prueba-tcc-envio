namespace TccEventos.Contratos.V1;

/// <summary>
/// Respuesta de GET /api/v1/guias/{numeroGuia}: estado actual y los eventos más recientes (máximo 100).
/// </summary>
/// <param name="NumeroGuia">Número de la guía consultada.</param>
/// <param name="EstadoActual">Estado actual en texto del contrato.</param>
/// <param name="UltimoEventoEn">Momento del último evento aplicado.</param>
/// <param name="Version">Número de cambios aplicados a la guía.</param>
/// <param name="Historial">Eventos recibidos, del más reciente al más antiguo, incluidos tardíos e inválidos.</param>
public record GuiaV1(
    string NumeroGuia,
    string EstadoActual,
    DateTimeOffset UltimoEventoEn,
    long Version,
    IReadOnlyList<EventoHistorialV1> Historial);

/// <summary>
/// Una línea del historial de la guía.
/// </summary>
/// <remarks>
/// Resultado: APLICADO (cambió el estado), TARDIO (llegó después de uno más reciente)
/// o TRANSICION_INVALIDA (no respeta la máquina de estados). Todos quedan para auditoría.
/// </remarks>
/// <param name="IdEvento">Identificador del evento.</param>
/// <param name="Estado">Estado que reportó el evento.</param>
/// <param name="OcurridoEn">Momento en que ocurrió.</param>
/// <param name="Origen">Sistema que lo reportó.</param>
/// <param name="Novedad">Descripción de la novedad, si la hay.</param>
/// <param name="Resultado">Qué pasó al aplicarlo: APLICADO, TARDIO o TRANSICION_INVALIDA.</param>
public record EventoHistorialV1(
    Guid IdEvento,
    string Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad,
    string Resultado);

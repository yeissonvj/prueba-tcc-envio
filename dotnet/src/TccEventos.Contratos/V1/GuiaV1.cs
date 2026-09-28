namespace TccEventos.Contratos.V1;

/// Respuesta de GET /api/v1/guias/{numeroGuia}: estado actual y los eventos más recientes (máximo 100).
public record GuiaV1(
    string NumeroGuia,
    string EstadoActual,
    DateTimeOffset UltimoEventoEn,
    long Version,
    IReadOnlyList<EventoHistorialV1> Historial);

/// Resultado: APLICADO (cambió el estado), TARDIO (llegó después de uno más reciente)
/// o TRANSICION_INVALIDA (no respeta la máquina de estados). Todos quedan para auditoría.
public record EventoHistorialV1(
    Guid IdEvento,
    string Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad,
    string Resultado);

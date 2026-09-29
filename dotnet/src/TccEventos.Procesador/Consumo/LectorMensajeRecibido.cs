using System.Text.Json;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Mapeo;

namespace TccEventos.Procesador.Consumo;

/// <summary>
/// Convierte el mensaje en un evento del dominio o explica por qué no se puede (poison pill).
/// </summary>
/// <remarks>Un mensaje ilegible nunca será legible: reintentarlo solo bloquearía la partición.</remarks>
public static class LectorMensajeRecibido
{
    /// <summary>Reglas de serialización web (camelCase).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lee y valida el contenido del mensaje.</summary>
    /// <param name="valor">Contenido del mensaje (JSON del contrato V1).</param>
    /// <returns>
    /// El evento y <c>Motivo</c> en <see langword="null"/> si es legible; o <c>Evento</c> en <see langword="null"/>
    /// y el motivo del rechazo si no lo es.
    /// </returns>
    public static (EventoGuia? Evento, string? Motivo) Leer(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return (null, "Mensaje vacío.");

        EventoGuiaV1? contrato;
        try
        {
            contrato = JsonSerializer.Deserialize<EventoGuiaV1>(valor, Json);
        }
        catch (JsonException ex)
        {
            return (null, $"JSON inválido: {ex.Message}");
        }

        if (contrato is null)
            return (null, "El mensaje es null.");
        if (contrato.IdEvento == Guid.Empty)
            return (null, "idEvento vacío.");
        if (string.IsNullOrWhiteSpace(contrato.NumeroGuia))
            return (null, "numeroGuia vacío.");
        if (string.IsNullOrWhiteSpace(contrato.Estado) || !MapeadorEventoGuia.EsEstadoValido(contrato.Estado))
            return (null, $"Estado no reconocido: '{contrato.Estado}'.");
        if (contrato.OcurridoEn == default)
            return (null, "ocurridoEn vacío.");
        if (string.IsNullOrWhiteSpace(contrato.Origen))
            return (null, "origen vacío.");

        return (MapeadorEventoGuia.ADominio(contrato), null);
    }
}

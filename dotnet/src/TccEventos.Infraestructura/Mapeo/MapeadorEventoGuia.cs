using TccEventos.Contratos.V1;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Mapeo;

/// <summary>
/// Traduce entre el formato externo (contrato V1) y el modelo del dominio.
/// </summary>
public static class MapeadorEventoGuia
{
    private static readonly Dictionary<EstadoGuia, string> ATexto = new()
    {
        [EstadoGuia.Creada] = EstadosV1.Creada,
        [EstadoGuia.Recogida] = EstadosV1.Recogida,
        [EstadoGuia.EnBodegaOrigen] = EstadosV1.EnBodegaOrigen,
        [EstadoGuia.EnTransito] = EstadosV1.EnTransito,
        [EstadoGuia.EnBodegaDestino] = EstadosV1.EnBodegaDestino,
        [EstadoGuia.EnReparto] = EstadosV1.EnReparto,
        [EstadoGuia.Entregada] = EstadosV1.Entregada,
        [EstadoGuia.Novedad] = EstadosV1.Novedad,
        [EstadoGuia.ReintentoEntrega] = EstadosV1.ReintentoEntrega,
        [EstadoGuia.Devuelta] = EstadosV1.Devuelta
    };

    private static readonly Dictionary<string, EstadoGuia> DesdeTexto =
        ATexto.ToDictionary(par => par.Value, par => par.Key);

    public static bool EsEstadoValido(string estado) => DesdeTexto.ContainsKey(estado);

    public static string EstadoATexto(EstadoGuia estado) => ATexto[estado];

    public static EstadoGuia EstadoDesdeTexto(string estado) => DesdeTexto[estado];

    public static string ResultadoATexto(ResultadoAplicacion resultado) => resultado switch
    {
        ResultadoAplicacion.Aplicado => "APLICADO",
        ResultadoAplicacion.Tardio => "TARDIO",
        ResultadoAplicacion.TransicionInvalida => "TRANSICION_INVALIDA",
        _ => throw new ArgumentOutOfRangeException(nameof(resultado), resultado, null)
    };

    public static EstadoGuiaCambiadoV1 ACambioEstado(Guia guia, EventoGuia causa, EstadoGuia? estadoAnterior) =>
        new(causa.IdEvento,
            guia.NumeroGuia,
            estadoAnterior is { } anterior ? ATexto[anterior] : null,
            ATexto[guia.EstadoActual],
            causa.OcurridoEn,
            causa.Origen,
            causa.Novedad,
            guia.Version);

    public static EventoGuia ADominio(EventoGuiaV1 c) =>
        new(c.IdEvento, c.NumeroGuia, DesdeTexto[c.Estado], c.OcurridoEn, c.Origen, c.Novedad);

    public static EventoGuiaV1 AContrato(EventoGuia e) =>
        new(e.IdEvento, e.NumeroGuia, ATexto[e.Estado], e.OcurridoEn, e.Origen, e.Novedad);
}
using TccEventos.Contratos.V1;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Mapeo;

/// <summary>
/// Traduce entre el formato externo (contrato V1) y el modelo del dominio.
/// </summary>
/// <remarks>
/// La tabla es explícita a propósito: renombrar un valor del enum del dominio no puede cambiar el contrato.
/// </remarks>
public static class MapeadorEventoGuia
{
    /// <summary>Texto del contrato para cada estado del dominio.</summary>
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

    /// <summary>Estado del dominio para cada texto del contrato (la tabla inversa).</summary>
    private static readonly Dictionary<string, EstadoGuia> DesdeTexto =
        ATexto.ToDictionary(par => par.Value, par => par.Key);

    /// <summary>Indica si un texto es un estado válido del contrato V1.</summary>
    /// <param name="estado">Texto a revisar.</param>
    /// <returns><see langword="true"/> si el estado existe.</returns>
    public static bool EsEstadoValido(string estado) => DesdeTexto.ContainsKey(estado);

    /// <summary>Convierte un estado del dominio en su texto del contrato.</summary>
    /// <param name="estado">Estado del dominio.</param>
    /// <returns>Por ejemplo <c>EN_REPARTO</c>.</returns>
    public static string EstadoATexto(EstadoGuia estado) => ATexto[estado];

    /// <summary>Convierte un texto del contrato en el estado del dominio.</summary>
    /// <param name="estado">Texto del contrato.</param>
    /// <returns>El estado del dominio.</returns>
    /// <exception cref="KeyNotFoundException">Si el texto no es un estado válido.</exception>
    public static EstadoGuia EstadoDesdeTexto(string estado) => DesdeTexto[estado];

    /// <summary>Convierte el resultado de aplicar un evento en el texto que se guarda en el historial.</summary>
    /// <param name="resultado">Resultado del dominio.</param>
    /// <returns>APLICADO, TARDIO o TRANSICION_INVALIDA.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si aparece un resultado nuevo sin traducción.</exception>
    public static string ResultadoATexto(ResultadoAplicacion resultado) => resultado switch
    {
        ResultadoAplicacion.Aplicado => "APLICADO",
        ResultadoAplicacion.Tardio => "TARDIO",
        ResultadoAplicacion.TransicionInvalida => "TRANSICION_INVALIDA",
        _ => throw new ArgumentOutOfRangeException(nameof(resultado), resultado, null)
    };

    /// <summary>Arma el mensaje de cambio de estado que va a la bandeja de salida.</summary>
    /// <param name="guia">Guía ya actualizada.</param>
    /// <param name="causa">Evento que provocó el cambio.</param>
    /// <param name="estadoAnterior">Estado previo; <see langword="null"/> si el cambio creó la guía.</param>
    /// <returns>El cambio en formato de contrato, con la versión de la guía.</returns>
    public static EstadoGuiaCambiadoV1 ACambioEstado(Guia guia, EventoGuia causa, EstadoGuia? estadoAnterior) =>
        new(causa.IdEvento,
            guia.NumeroGuia,
            estadoAnterior is { } anterior ? ATexto[anterior] : null,
            ATexto[guia.EstadoActual],
            causa.OcurridoEn,
            causa.Origen,
            causa.Novedad,
            guia.Version);

    /// <summary>Convierte un evento del contrato en un evento del dominio.</summary>
    /// <param name="c">Evento en formato de contrato (ya validado).</param>
    /// <returns>El evento del dominio.</returns>
    public static EventoGuia ADominio(EventoGuiaV1 c) =>
        new(c.IdEvento, c.NumeroGuia, DesdeTexto[c.Estado], c.OcurridoEn, c.Origen, c.Novedad);

    /// <summary>Convierte un evento del dominio en su formato de contrato.</summary>
    /// <param name="e">Evento del dominio.</param>
    /// <returns>El evento en formato de contrato, listo para serializar.</returns>
    public static EventoGuiaV1 AContrato(EventoGuia e) =>
        new(e.IdEvento, e.NumeroGuia, ATexto[e.Estado], e.OcurridoEn, e.Origen, e.Novedad);
}

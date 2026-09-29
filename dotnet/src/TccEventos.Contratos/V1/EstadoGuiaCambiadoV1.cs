namespace TccEventos.Contratos.V1;

/// <summary>
/// Mensaje de guias.estados.cambiados: solo cambios validados y aplicados.
/// </summary>
/// <remarks>
/// <see cref="Version"/> es consecutiva por guía: un consumidor puede descartar cualquier mensaje con versión menor
/// o igual a la última que procesó, y usar (<see cref="NumeroGuia"/>, <see cref="Version"/>) como llave de idempotencia.
/// </remarks>
/// <param name="IdEvento">Evento que provocó el cambio.</param>
/// <param name="NumeroGuia">Guía que cambió; es la clave del mensaje en Kafka.</param>
/// <param name="EstadoAnterior">Estado antes del cambio en texto del contrato; <see langword="null"/> si el cambio creó la guía.</param>
/// <param name="EstadoNuevo">Estado después del cambio en texto del contrato (por ejemplo <c>EN_REPARTO</c>).</param>
/// <param name="OcurridoEn">Momento en que ocurrió el evento.</param>
/// <param name="Origen">Sistema que reportó el evento.</param>
/// <param name="Novedad">Descripción de la novedad, si la hay.</param>
/// <param name="Version">Versión de la guía tras el cambio.</param>
public record EstadoGuiaCambiadoV1(
    Guid IdEvento,
    string NumeroGuia,
    string? EstadoAnterior,
    string EstadoNuevo,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad,
    long Version)
{
    /// <summary>Nombre del contrato; viaja en el encabezado <c>contrato</c> del mensaje de Kafka.</summary>
    public const string Tipo = "EstadoGuiaCambiadoV1";
}

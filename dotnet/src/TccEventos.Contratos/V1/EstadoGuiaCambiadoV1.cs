namespace TccEventos.Contratos.V1;

/// Mensaje de guias.estados.cambiados: solo cambios validados y aplicados.
/// Version es consecutiva por guía: un consumidor puede descartar cualquier mensaje con versión menor
/// o igual a la última que procesó, y usar (NumeroGuia, Version) como llave de idempotencia.
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
    public const string Tipo = "EstadoGuiaCambiadoV1";
}

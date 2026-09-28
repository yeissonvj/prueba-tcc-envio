using TccEventos.Contratos.V1;

namespace TccEventos.Api.Validacion;

/// Valida la forma del evento en la frontera. Las reglas de negocio
/// (transiciones, eventos tardíos) NO van aquí: las decide el procesador.
public class ValidadorEventoGuiaV1(TimeProvider reloj)
{
    public const int LongitudMaximaGuia = 30;
    public const int LongitudMaximaOrigen = 30;
    public const int LongitudMaximaNovedad = 500;

    // Tolerancia por relojes desfasados en dispositivos de mensajeros.
    public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromMinutes(5);

    /// Misma regla para la ingesta y la consulta: un número que no puede recibirse tampoco se consulta.
    public static bool EsNumeroGuiaValido(string? numeroGuia) =>
        !string.IsNullOrWhiteSpace(numeroGuia)
        && numeroGuia.Length <= LongitudMaximaGuia
        && numeroGuia.All(char.IsAsciiLetterOrDigit);

    public Dictionary<string, string[]> Validar(EventoGuiaV1 evento)
    {
        var errores = new Dictionary<string, string[]>();

        if (evento.IdEvento == Guid.Empty)
            errores["idEvento"] = ["Es obligatorio y no puede ser vacío."];

        if (string.IsNullOrWhiteSpace(evento.NumeroGuia))
            errores["numeroGuia"] = ["Es obligatorio."];
        else if (!EsNumeroGuiaValido(evento.NumeroGuia))
            errores["numeroGuia"] = [$"Solo letras y números, máximo {LongitudMaximaGuia} caracteres."];

        if (string.IsNullOrWhiteSpace(evento.Estado))
            errores["estado"] = ["Es obligatorio."];
        else if (!EstadosV1.Todos.Contains(evento.Estado))
            errores["estado"] = [$"Valor no reconocido. Permitidos: {string.Join(", ", EstadosV1.Todos)}."];

        if (evento.OcurridoEn == default)
            errores["ocurridoEn"] = ["Es obligatorio."];
        else if (evento.OcurridoEn > reloj.GetUtcNow() + ToleranciaFuturo)
            errores["ocurridoEn"] = ["No puede estar en el futuro."];

        if (string.IsNullOrWhiteSpace(evento.Origen))
            errores["origen"] = ["Es obligatorio."];
        else if (evento.Origen.Length > LongitudMaximaOrigen)
            errores["origen"] = [$"Máximo {LongitudMaximaOrigen} caracteres."];

        if (evento.Estado == EstadosV1.Novedad && string.IsNullOrWhiteSpace(evento.Novedad))
            errores["novedad"] = ["Es obligatoria cuando el estado es NOVEDAD."];
        else if (evento.Novedad?.Length > LongitudMaximaNovedad)
            errores["novedad"] = [$"Máximo {LongitudMaximaNovedad} caracteres."];

        return errores;
    }
}

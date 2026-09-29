using TccEventos.Contratos.V1;

namespace TccEventos.Api.Validacion;

/// <summary>
/// Valida la forma del evento en la frontera de la API.
/// </summary>
/// <remarks>
/// Las reglas de negocio (transiciones, eventos tardíos) NO van aquí: las decide el procesador.
/// </remarks>
/// <param name="reloj">Reloj para validar que la fecha no esté en el futuro (reemplazable en pruebas).</param>
public class ValidadorEventoGuiaV1(TimeProvider reloj)
{
    /// <summary>Longitud máxima del número de guía.</summary>
    public const int LongitudMaximaGuia = 30;

    /// <summary>Longitud máxima del origen.</summary>
    public const int LongitudMaximaOrigen = 30;

    /// <summary>Longitud máxima de la descripción de la novedad.</summary>
    public const int LongitudMaximaNovedad = 500;

    // Tolerancia por relojes desfasados en dispositivos de mensajeros.
    /// <summary>Cuánto puede estar en el futuro la fecha del evento sin rechazarse.</summary>
    public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Indica si un número de guía es válido: letras y números ASCII, máximo 30 caracteres.
    /// </summary>
    /// <remarks>Misma regla para la ingesta y la consulta: un número que no puede recibirse tampoco se consulta.</remarks>
    /// <param name="numeroGuia">Número a revisar.</param>
    /// <returns><see langword="true"/> si es válido.</returns>
    public static bool EsNumeroGuiaValido(string? numeroGuia) =>
        !string.IsNullOrWhiteSpace(numeroGuia)
        && numeroGuia.Length <= LongitudMaximaGuia
        && numeroGuia.All(char.IsAsciiLetterOrDigit);

    /// <summary>Valida todos los campos del evento y reporta todos los errores a la vez.</summary>
    /// <param name="evento">Evento recibido.</param>
    /// <returns>Errores por campo (vacío si el evento es válido), en el formato de ValidationProblem.</returns>
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

using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Puerto: guarda y consulta guías y su historial de eventos.
/// </summary>
public interface IRepositorioGuias
{
    /// <summary>Indica si un evento ya se procesó (consulta el inbox).</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si el evento ya está en el historial.</returns>
    Task<bool> ExisteEventoAsync(Guid idEvento, CancellationToken ct);

    /// <summary>Carga una guía con su estado actual.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>La guía, o <see langword="null"/> si todavía no existe.</returns>
    Task<Guia?> ObtenerAsync(string numeroGuia, CancellationToken ct);

    /// <summary>
    /// En una sola transacción: registra el evento en el historial y,
    /// si fue aplicado, guarda el nuevo estado y deja el cambio listo para publicarse.
    /// </summary>
    /// <param name="guia">Guía con el estado ya actualizado en memoria.</param>
    /// <param name="evento">Evento que se procesó.</param>
    /// <param name="resultado">Resultado de aplicar el evento.</param>
    /// <param name="estadoAnterior">Estado previo; es <see langword="null"/> cuando el evento creó la guía.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la transacción se confirmó.</returns>
    /// <exception cref="ConflictoConcurrenciaException">
    /// Otra instancia escribió la guía o el evento entre la lectura y la escritura.
    /// </exception>
    Task GuardarAsync(Guia guia, EventoGuia evento, ResultadoAplicacion resultado, EstadoGuia? estadoAnterior, CancellationToken ct);
}

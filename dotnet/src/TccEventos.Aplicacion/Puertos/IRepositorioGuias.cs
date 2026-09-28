using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Guarda y consulta guías y su historial de eventos.
/// </summary>
public interface IRepositorioGuias
{
    Task<bool> ExisteEventoAsync(Guid idEvento, CancellationToken ct);

    Task<Guia?> ObtenerAsync(string numeroGuia, CancellationToken ct);

    /// <summary>
    /// En una sola transacción: registra el evento en el historial y,
    /// si fue aplicado, guarda el nuevo estado y deja el cambio listo para publicarse.
    /// <paramref name="estadoAnterior"/> es null cuando el evento creó la guía.
    /// Lanza <see cref="ConflictoConcurrenciaException"/> si otra instancia escribió la guía o el evento
    /// entre la lectura y la escritura.
    /// </summary>
    Task GuardarAsync(Guia guia, EventoGuia evento, ResultadoAplicacion resultado, EstadoGuia? estadoAnterior, CancellationToken ct);
}

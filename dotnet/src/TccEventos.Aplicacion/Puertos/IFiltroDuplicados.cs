namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Puerto: detecta rápido eventos ya recibidos.
/// </summary>
/// <remarks>
/// Es una optimización, no la garantía: si falla o expira, el procesador sigue deduplicando con su inbox.
/// </remarks>
public interface IFiltroDuplicados
{
    /// <summary>Indica si el evento ya se recibió antes.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si el evento ya se había recibido.</returns>
    Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct);

    /// <summary>Marca el evento como recibido.</summary>
    /// <param name="idEvento">Identificador del evento.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el evento quedó marcado.</returns>
    Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct);
}

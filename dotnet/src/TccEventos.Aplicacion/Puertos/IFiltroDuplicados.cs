namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Detecta rápido eventos ya recibidos. Es una optimización, no la garantía:
/// si falla o expira, el procesador sigue deduplicando.
/// </summary>
public interface IFiltroDuplicados
{
    Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct);
    Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct);
}
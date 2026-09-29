namespace TccEventos.Api.Salud;

/// <summary>
/// Revisa si una dependencia externa está disponible. Nunca lanza: una falla es "false".
/// </summary>
/// <remarks>Cada implementación limita su propio tiempo para que la sonda responda rápido.</remarks>
public interface ISonda
{
    /// <summary>Indica si la dependencia está disponible.</summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns><see langword="true"/> si está disponible.</returns>
    Task<bool> DisponibleAsync(CancellationToken ct);
}

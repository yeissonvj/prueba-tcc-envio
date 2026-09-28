namespace TccEventos.Api.Salud;

/// Revisa si una dependencia externa está disponible. Nunca lanza: una falla es "false".
/// Cada implementación limita su propio tiempo para que la sonda responda rápido.
public interface ISonda
{
    Task<bool> DisponibleAsync(CancellationToken ct);
}

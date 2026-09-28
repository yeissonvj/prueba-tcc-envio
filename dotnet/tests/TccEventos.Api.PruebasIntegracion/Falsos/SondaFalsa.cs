using TccEventos.Api.Salud;

namespace TccEventos.Api.PruebasIntegracion.Falsos;

public class SondaFalsa(bool disponible = true) : ISonda
{
    public bool Disponible { get; set; } = disponible;

    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.FromResult(Disponible);
}

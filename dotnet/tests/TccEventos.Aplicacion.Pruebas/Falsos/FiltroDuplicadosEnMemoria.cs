using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class FiltroDuplicadosEnMemoria : IFiltroDuplicados
{
    private readonly HashSet<Guid> _recibidos = [];

    public Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        Task.FromResult(_recibidos.Contains(idEvento));

    public Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct)
    {
        _recibidos.Add(idEvento);
        return Task.CompletedTask;
    }
}
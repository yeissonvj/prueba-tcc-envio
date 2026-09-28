using System.Collections.Concurrent;
using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Api.PruebasIntegracion.Falsos;

public class FiltroDuplicadosEnMemoria : IFiltroDuplicados
{
    private readonly ConcurrentDictionary<Guid, bool> _recibidos = new();

    public Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        Task.FromResult(_recibidos.ContainsKey(idEvento));

    public Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct)
    {
        _recibidos[idEvento] = true;
        return Task.CompletedTask;
    }
}

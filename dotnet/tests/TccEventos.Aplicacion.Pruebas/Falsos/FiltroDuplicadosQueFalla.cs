using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class FiltroDuplicadosQueFalla : IFiltroDuplicados
{
    public Task<bool> YaRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        throw new TimeoutException("Redis no responde");

    public Task MarcarRecibidoAsync(Guid idEvento, CancellationToken ct) =>
        throw new TimeoutException("Redis no responde");
}
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class RepositorioGuiasEnMemoria : IRepositorioGuias
{
    public Dictionary<string, Guia> Guias { get; } = [];
    public List<(EventoGuia Evento, ResultadoAplicacion Resultado)> Historial { get; } = [];

    /// Lo que el repositorio real dejaría en la bandeja de salida.
    public List<(EstadoGuia? Anterior, EstadoGuia Nuevo, long Version)> Cambios { get; } = [];

    public Task<bool> ExisteEventoAsync(Guid idEvento, CancellationToken ct) =>
        Task.FromResult(Historial.Any(h => h.Evento.IdEvento == idEvento));

    public Task<Guia?> ObtenerAsync(string numeroGuia, CancellationToken ct) =>
        Task.FromResult(Guias.GetValueOrDefault(numeroGuia));

    public Task GuardarAsync(Guia guia, EventoGuia evento, ResultadoAplicacion resultado, EstadoGuia? estadoAnterior, CancellationToken ct)
    {
        Historial.Add((evento, resultado));
        if (resultado == ResultadoAplicacion.Aplicado)
        {
            Guias[guia.NumeroGuia] = guia;
            Cambios.Add((estadoAnterior, guia.EstadoActual, guia.Version));
        }
        return Task.CompletedTask;
    }
}

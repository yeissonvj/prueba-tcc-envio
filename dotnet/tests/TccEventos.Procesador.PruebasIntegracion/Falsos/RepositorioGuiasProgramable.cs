using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Procesador.PruebasIntegracion.Falsos;

/// Repositorio en memoria al que se le puede ordenar fallar las próximas N escrituras con una excepción dada.
public class RepositorioGuiasProgramable : IRepositorioGuias
{
    private readonly Dictionary<string, Guia> _guias = [];
    public List<EventoGuia> Guardados { get; } = [];
    public int IntentosDeGuardar { get; private set; }

    private int _fallasPendientes;
    private Func<Exception>? _falla;

    public void FallarProximas(int veces, Func<Exception> falla) => (_fallasPendientes, _falla) = (veces, falla);

    public Task<bool> ExisteEventoAsync(Guid idEvento, CancellationToken ct) =>
        Task.FromResult(Guardados.Any(e => e.IdEvento == idEvento));

    public Task<Guia?> ObtenerAsync(string numeroGuia, CancellationToken ct) =>
        Task.FromResult(_guias.GetValueOrDefault(numeroGuia));

    public Task GuardarAsync(Guia guia, EventoGuia evento, ResultadoAplicacion resultado, EstadoGuia? estadoAnterior, CancellationToken ct)
    {
        IntentosDeGuardar++;
        if (_fallasPendientes > 0)
        {
            _fallasPendientes--;
            throw _falla!();
        }

        Guardados.Add(evento);
        if (resultado == ResultadoAplicacion.Aplicado)
            _guias[guia.NumeroGuia] = guia;
        return Task.CompletedTask;
    }
}

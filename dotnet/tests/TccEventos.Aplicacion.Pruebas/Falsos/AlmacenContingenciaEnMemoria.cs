using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class AlmacenContingenciaEnMemoria : IAlmacenContingencia
{
    public List<EventoGuia> Pendientes { get; } = [];
    public bool Fallar { get; set; }

    public Task GuardarAsync(EventoGuia evento, CancellationToken ct)
    {
        if (Fallar) throw new TimeoutException("PostgreSQL no responde");
        if (Pendientes.All(e => e.IdEvento != evento.IdEvento))
            Pendientes.Add(evento);
        return Task.CompletedTask;
    }

    public async Task<int> ReenviarPendientesAsync(
        Func<EventoGuia, CancellationToken, Task> publicar, int maximo, CancellationToken ct)
    {
        var publicados = new List<EventoGuia>();
        foreach (var evento in Pendientes.Take(maximo).ToList())
        {
            try
            {
                await publicar(evento, ct);
                publicados.Add(evento);
            }
            catch (PublicacionFallidaException) { }
        }

        Pendientes.RemoveAll(publicados.Contains);
        return publicados.Count;
    }
}

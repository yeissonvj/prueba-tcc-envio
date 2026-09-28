using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas.Falsos;

public class PublicadorFalso : IPublicadorEventos
{
    public List<EventoGuia> Publicados { get; } = [];
    public bool Fallar { get; set; }

    public Task PublicarAsync(EventoGuia evento, CancellationToken ct)
    {
        if (Fallar) throw new PublicacionFallidaException("Broker no disponible");
        Publicados.Add(evento);
        return Task.CompletedTask;
    }
}
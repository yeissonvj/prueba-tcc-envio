using System.Collections.Concurrent;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Api.PruebasIntegracion.Falsos;

public class PublicadorEnMemoria : IPublicadorEventos
{
    public ConcurrentQueue<EventoGuia> Publicados { get; } = new();
    public bool Fallar { get; set; }
    public int Intentos { get; private set; }

    public Task PublicarAsync(EventoGuia evento, CancellationToken ct)
    {
        Intentos++;
        if (Fallar) throw new PublicacionFallidaException("Kafka no disponible (simulado)");
        Publicados.Enqueue(evento);
        return Task.CompletedTask;
    }
}

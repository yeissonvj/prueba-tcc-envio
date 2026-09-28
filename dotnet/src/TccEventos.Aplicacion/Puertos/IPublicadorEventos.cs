using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Publica eventos para su procesamiento posterior.
/// Contrato: solo termina con éxito cuando el evento quedó guardado de forma durable.
/// Si no puede garantizarlo, lanza una excepción.
/// </summary>
public interface IPublicadorEventos
{
    Task PublicarAsync(EventoGuia evento, CancellationToken ct);
}
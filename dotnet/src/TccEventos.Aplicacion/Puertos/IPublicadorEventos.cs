using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Puerto: publica eventos para su procesamiento posterior.
/// </summary>
/// <remarks>
/// Contrato: solo termina con éxito cuando el evento quedó guardado de forma durable.
/// Si no puede garantizarlo, lanza una excepción.
/// </remarks>
public interface IPublicadorEventos
{
    /// <summary>Publica un evento de forma durable.</summary>
    /// <param name="evento">Evento a publicar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el evento quedó guardado.</returns>
    /// <exception cref="PublicacionFallidaException">El evento no quedó guardado de forma durable.</exception>
    Task PublicarAsync(EventoGuia evento, CancellationToken ct);
}

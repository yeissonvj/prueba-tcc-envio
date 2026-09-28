using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// Almacén durable para cuando el broker no confirma. Debe tolerar varias instancias en paralelo.
public interface IAlmacenContingencia
{
    /// Solo termina con éxito si el evento quedó guardado. Idempotente por IdEvento.
    Task GuardarAsync(EventoGuia evento, CancellationToken ct);

    /// Entrega hasta <paramref name="maximo"/> pendientes, en orden de llegada, a <paramref name="publicar"/>
    /// y elimina los que se publicaron; los que fallan quedan para el siguiente ciclo.
    /// Devuelve cuántos se reenviaron.
    Task<int> ReenviarPendientesAsync(Func<EventoGuia, CancellationToken, Task> publicar, int maximo, CancellationToken ct);
}

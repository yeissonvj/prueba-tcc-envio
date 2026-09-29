using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Puerto: almacén durable para cuando el broker no confirma. Debe tolerar varias instancias en paralelo.
/// </summary>
public interface IAlmacenContingencia
{
    /// <summary>Guarda un evento en la contingencia. Idempotente por <see cref="EventoGuia.IdEvento"/>.</summary>
    /// <param name="evento">Evento a guardar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que solo termina con éxito si el evento quedó guardado.</returns>
    Task GuardarAsync(EventoGuia evento, CancellationToken ct);

    /// <summary>
    /// Entrega hasta <paramref name="maximo"/> pendientes, en orden de llegada, a <paramref name="publicar"/>
    /// y elimina los que se publicaron; los que fallan quedan para el siguiente ciclo.
    /// </summary>
    /// <param name="publicar">Función que publica cada evento en el broker.</param>
    /// <param name="maximo">Tamaño máximo del lote.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Cuántos eventos se reenviaron.</returns>
    Task<int> ReenviarPendientesAsync(Func<EventoGuia, CancellationToken, Task> publicar, int maximo, CancellationToken ct);
}

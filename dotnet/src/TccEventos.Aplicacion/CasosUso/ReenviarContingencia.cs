using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Aplicacion.CasosUso;

/// Vacía la contingencia hacia el broker. El publicador que recibe debe ser el directo
/// (sin contingencia): si no, un evento que falla volvería a la misma tabla de la que salió.
public class ReenviarContingencia(IAlmacenContingencia almacen, IPublicadorEventos publicadorDirecto)
{
    public Task<int> EjecutarAsync(int maximo, CancellationToken ct) =>
        almacen.ReenviarPendientesAsync(publicadorDirecto.PublicarAsync, maximo, ct);
}

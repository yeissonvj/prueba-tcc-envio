using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Notificaciones;

/// <summary>
/// Directorio de contactos ficticio y determinista (en producción: el servicio de clientes de TCC).
/// </summary>
/// <remarks>
/// Las guías que terminan en 0 no tienen teléfono registrado, para ejercitar el canal alterno.
/// </remarks>
public sealed class DirectorioContactosSimulado : IDirectorioContactos
{
    /// <summary>Genera un contacto a partir del número de guía (siempre el mismo para la misma guía).</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="ct">Token de cancelación (no se usa).</param>
    /// <returns>Un contacto con correo y, salvo si la guía termina en 0, teléfono.</returns>
    public Task<Contacto?> ObtenerAsync(string numeroGuia, CancellationToken ct)
    {
        // Hash estable: string.GetHashCode cambia en cada ejecución del proceso en .NET.
        var hash = numeroGuia.Aggregate(17L, (acumulado, c) => (acumulado * 31 + c) % 1_000_000_007);
        var sufijo = (hash % 10_000_000).ToString("D7");
        var telefono = numeroGuia.EndsWith('0') ? null : $"300{sufijo}";
        return Task.FromResult<Contacto?>(new Contacto(telefono, $"cliente.{numeroGuia.ToLowerInvariant()}@correo.test"));
    }
}

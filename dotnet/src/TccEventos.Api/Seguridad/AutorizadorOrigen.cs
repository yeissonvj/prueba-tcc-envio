using System.Security.Claims;

namespace TccEventos.Api.Seguridad;

/// Autorización sobre los datos: un cliente solo puede reportar eventos de los orígenes que le pertenecen.
/// Sin esto, un sistema comprometido podría hacerse pasar por otro en el historial de la guía.
public class AutorizadorOrigen(OpcionesSeguridad opciones)
{
    public bool PuedeReportar(ClaimsPrincipal usuario, string origen)
    {
        var cliente = usuario.FindFirst(Reclamos.Cliente)?.Value;

        return cliente is not null
            && opciones.OrigenesPorCliente.TryGetValue(cliente, out var permitidos)
            && permitidos.Contains(origen, StringComparer.Ordinal);
    }
}

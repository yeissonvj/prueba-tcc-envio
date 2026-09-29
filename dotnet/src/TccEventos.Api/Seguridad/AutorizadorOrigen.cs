using System.Security.Claims;

namespace TccEventos.Api.Seguridad;

/// <summary>
/// Autorización sobre los datos: un cliente solo puede reportar eventos de los orígenes que le pertenecen.
/// </summary>
/// <remarks>Sin esto, un sistema comprometido podría hacerse pasar por otro en el historial de la guía.</remarks>
/// <param name="opciones">Orígenes permitidos por cliente.</param>
public class AutorizadorOrigen(OpcionesSeguridad opciones)
{
    /// <summary>Indica si el cliente autenticado puede reportar eventos con ese origen.</summary>
    /// <param name="usuario">Cliente autenticado (claim azp del token).</param>
    /// <param name="origen">Origen que trae el evento.</param>
    /// <returns><see langword="true"/> si el origen pertenece al cliente.</returns>
    public bool PuedeReportar(ClaimsPrincipal usuario, string origen)
    {
        var cliente = usuario.FindFirst(Reclamos.Cliente)?.Value;

        return cliente is not null
            && opciones.OrigenesPorCliente.TryGetValue(cliente, out var permitidos)
            && permitidos.Contains(origen, StringComparer.Ordinal);
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Define qué cambios de estado son válidos para una guía.
/// </summary>
/// <remarks>
/// Cualquier estado no final puede pasar a <see cref="EstadoGuia.Novedad"/>.
/// <see cref="EstadoGuia.Entregada"/> y <see cref="EstadoGuia.Devuelta"/> son finales: no aceptan más cambios.
/// </remarks>
public static class MaquinaEstados
{
    /// <summary>Tabla de transiciones: para cada estado, los estados a los que puede pasar.</summary>
    private static readonly Dictionary<EstadoGuia, EstadoGuia[]> Transiciones = new()
    {
        [EstadoGuia.Creada] = [EstadoGuia.Recogida, EstadoGuia.Novedad],
        [EstadoGuia.Recogida] = [EstadoGuia.EnBodegaOrigen, EstadoGuia.Novedad],
        [EstadoGuia.EnBodegaOrigen] = [EstadoGuia.EnTransito, EstadoGuia.Novedad],
        [EstadoGuia.EnTransito] = [EstadoGuia.EnBodegaDestino, EstadoGuia.Novedad],
        [EstadoGuia.EnBodegaDestino] = [EstadoGuia.EnReparto, EstadoGuia.Novedad],
        [EstadoGuia.EnReparto] = [EstadoGuia.Entregada, EstadoGuia.Novedad],
        [EstadoGuia.Novedad] = [EstadoGuia.ReintentoEntrega, EstadoGuia.Devuelta],
        [EstadoGuia.ReintentoEntrega] = [EstadoGuia.EnReparto, EstadoGuia.Novedad],
        [EstadoGuia.Entregada] = [],
        [EstadoGuia.Devuelta] = []
    };

    /// <summary>Indica si una guía puede pasar de un estado a otro.</summary>
    /// <param name="desde">Estado actual de la guía.</param>
    /// <param name="hacia">Estado al que se quiere pasar.</param>
    /// <returns><see langword="true"/> si la transición está permitida.</returns>
    public static bool PuedeTransitar(EstadoGuia desde, EstadoGuia hacia) =>
        Transiciones[desde].Contains(hacia);

    /// <summary>Indica si un estado es final, es decir, si ya no admite más cambios.</summary>
    /// <param name="estado">Estado a revisar.</param>
    /// <returns><see langword="true"/> para <see cref="EstadoGuia.Entregada"/> y <see cref="EstadoGuia.Devuelta"/>.</returns>
    public static bool EsEstadoFinal(EstadoGuia estado) =>
        Transiciones[estado].Length == 0;
}

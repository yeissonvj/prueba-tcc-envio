using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Define qué cambios de estado son válidos para una guía.
/// </summary>
public static class MaquinaEstados
{
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

    public static bool PuedeTransitar(EstadoGuia desde, EstadoGuia hacia) =>
        Transiciones[desde].Contains(hacia);

    public static bool EsEstadoFinal(EstadoGuia estado) =>
        Transiciones[estado].Length == 0;
}

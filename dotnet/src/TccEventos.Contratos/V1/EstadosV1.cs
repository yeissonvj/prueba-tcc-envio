using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Contratos.V1;

/// <summary>
/// Textos de estado acordados con los sistemas de TCC en la versión 1 del contrato.
/// </summary>
public static class EstadosV1
{
    public const string Creada = "CREADA";
    public const string Recogida = "RECOGIDA";
    public const string EnBodegaOrigen = "EN_BODEGA_ORIGEN";
    public const string EnTransito = "EN_TRANSITO";
    public const string EnBodegaDestino = "EN_BODEGA_DESTINO";
    public const string EnReparto = "EN_REPARTO";
    public const string Entregada = "ENTREGADA";
    public const string Novedad = "NOVEDAD";
    public const string ReintentoEntrega = "REINTENTO_ENTREGA";
    public const string Devuelta = "DEVUELTA";

    public static readonly string[] Todos =
    [
        Creada, Recogida, EnBodegaOrigen, EnTransito, EnBodegaDestino,
        EnReparto, Entregada, Novedad, ReintentoEntrega, Devuelta
    ];
}

using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Contratos.V1;

/// <summary>
/// Textos de estado acordados con los sistemas de TCC en la versión 1 del contrato.
/// </summary>
/// <remarks>Cambiar uno de estos textos rompe a los emisores: solo se pueden agregar valores nuevos.</remarks>
public static class EstadosV1
{
    /// <summary>La guía fue creada.</summary>
    public const string Creada = "CREADA";

    /// <summary>El envío fue recogido.</summary>
    public const string Recogida = "RECOGIDA";

    /// <summary>El envío está en la bodega de origen.</summary>
    public const string EnBodegaOrigen = "EN_BODEGA_ORIGEN";

    /// <summary>El envío está en tránsito entre ciudades.</summary>
    public const string EnTransito = "EN_TRANSITO";

    /// <summary>El envío está en la bodega de destino.</summary>
    public const string EnBodegaDestino = "EN_BODEGA_DESTINO";

    /// <summary>El envío salió a reparto.</summary>
    public const string EnReparto = "EN_REPARTO";

    /// <summary>El envío fue entregado.</summary>
    public const string Entregada = "ENTREGADA";

    /// <summary>Hubo una novedad en la entrega; exige el campo novedad.</summary>
    public const string Novedad = "NOVEDAD";

    /// <summary>Se programa un nuevo intento de entrega.</summary>
    public const string ReintentoEntrega = "REINTENTO_ENTREGA";

    /// <summary>El envío se devolvió al remitente.</summary>
    public const string Devuelta = "DEVUELTA";

    /// <summary>Todos los estados válidos del contrato V1; la API rechaza cualquier otro con 400.</summary>
    public static readonly string[] Todos =
    [
        Creada, Recogida, EnBodegaOrigen, EnTransito, EnBodegaDestino,
        EnReparto, Entregada, Novedad, ReintentoEntrega, Devuelta
    ];
}

using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Estados por los que puede pasar una guía de TCC durante su recorrido.
/// </summary>
/// <remarks>
/// Las transiciones permitidas entre estos valores las define <see cref="MaquinaEstados"/>.
/// En el contrato externo se escriben en mayúsculas (por ejemplo <c>EN_REPARTO</c>).
/// </remarks>
public enum EstadoGuia
{
    /// <summary>La guía fue creada en el sistema; el envío aún no se recoge.</summary>
    Creada,

    /// <summary>El mensajero recogió el envío donde el remitente.</summary>
    Recogida,

    /// <summary>El envío está en la bodega de la ciudad de origen.</summary>
    EnBodegaOrigen,

    /// <summary>El envío viaja entre la ciudad de origen y la de destino.</summary>
    EnTransito,

    /// <summary>El envío llegó a la bodega de la ciudad de destino.</summary>
    EnBodegaDestino,

    /// <summary>El envío salió a la ruta de entrega final.</summary>
    EnReparto,

    /// <summary>El envío fue entregado al destinatario. Estado final.</summary>
    Entregada,

    /// <summary>Ocurrió un problema en la entrega (dirección errada, ausencia, rechazo).</summary>
    Novedad,

    /// <summary>Se programa un nuevo intento de entrega después de una novedad.</summary>
    ReintentoEntrega,

    /// <summary>El envío se devolvió al remitente. Estado final.</summary>
    Devuelta
}

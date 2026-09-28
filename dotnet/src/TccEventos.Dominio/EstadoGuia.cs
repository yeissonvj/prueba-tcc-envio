using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Estados por los que puede pasar una guía de TCC.
/// </summary>
public enum EstadoGuia
{
    Creada,
    Recogida,
    EnBodegaOrigen,
    EnTransito,
    EnBodegaDestino,
    EnReparto,
    Entregada,
    Novedad,
    ReintentoEntrega,
    Devuelta
}

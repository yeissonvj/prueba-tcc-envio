using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Contratos.V1;

/// <summary>
/// Evento de estado de una guía, tal como lo envían los sistemas origen.
/// </summary>
public record EventoGuiaV1(
    Guid IdEvento,
    string NumeroGuia,
    string Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad = null);
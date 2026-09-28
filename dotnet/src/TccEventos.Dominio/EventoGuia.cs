using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Algo que le ocurrió a una guía en un momento dado.
/// Es inmutable: un hecho que ya pasó no se modifica.
/// </summary>
public record EventoGuia(
    Guid IdEvento,
    string NumeroGuia,
    EstadoGuia Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad = null);
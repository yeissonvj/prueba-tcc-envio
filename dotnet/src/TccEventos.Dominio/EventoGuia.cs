using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Algo que le ocurrió a una guía en un momento dado.
/// Es inmutable: un hecho que ya pasó no se modifica.
/// </summary>
/// <param name="IdEvento">Identificador único del evento; es la llave de idempotencia en todo el sistema.</param>
/// <param name="NumeroGuia">Número de la guía a la que pertenece el evento; también es la clave de partición en Kafka.</param>
/// <param name="Estado">Estado que reporta el evento.</param>
/// <param name="OcurridoEn">Momento en que ocurrió el hecho según el sistema emisor (con su huso horario).</param>
/// <param name="Origen">Sistema que reportó el evento (TMS, TRANSPORTE, ...).</param>
/// <param name="Novedad">Descripción de la novedad; solo aplica cuando <paramref name="Estado"/> es <see cref="EstadoGuia.Novedad"/>.</param>
public record EventoGuia(
    Guid IdEvento,
    string NumeroGuia,
    EstadoGuia Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad = null);

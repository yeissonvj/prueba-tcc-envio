using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Contratos.V1;

/// <summary>
/// Evento de estado de una guía, tal como lo envían los sistemas origen
/// (cuerpo de POST /api/v1/eventos-guia y mensaje de guias.eventos.recibidos).
/// </summary>
/// <param name="IdEvento">Identificador único que genera el emisor; reintentar siempre con el mismo.</param>
/// <param name="NumeroGuia">Número de la guía: solo letras y números, máximo 30 caracteres.</param>
/// <param name="Estado">Estado en texto del contrato (ver <see cref="EstadosV1"/>).</param>
/// <param name="OcurridoEn">Momento en que ocurrió el hecho, con su huso horario.</param>
/// <param name="Origen">Sistema emisor; debe corresponder al cliente autenticado.</param>
/// <param name="Novedad">Descripción de la novedad; obligatoria cuando el estado es NOVEDAD.</param>
public record EventoGuiaV1(
    Guid IdEvento,
    string NumeroGuia,
    string Estado,
    DateTimeOffset OcurridoEn,
    string Origen,
    string? Novedad = null);

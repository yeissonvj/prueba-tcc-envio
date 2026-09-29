using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Qué ocurrió al intentar aplicar un evento a una guía.
/// </summary>
/// <remarks>
/// Es un resultado explícito en lugar de una excepción: tardíos e inválidos son parte normal del negocio
/// y quedan en el historial para auditoría.
/// </remarks>
public enum ResultadoAplicacion
{
    /// <summary>El estado de la guía cambió.</summary>
    Aplicado,

    /// <summary>El evento es anterior al último aplicado: se guarda en historial, no cambia el estado.</summary>
    Tardio,

    /// <summary>El cambio de estado no está permitido.</summary>
    TransicionInvalida
}

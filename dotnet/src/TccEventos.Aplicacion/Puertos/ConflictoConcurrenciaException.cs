namespace TccEventos.Aplicacion.Puertos;

/// <summary>
/// Otra instancia modificó la guía entre la lectura y la escritura (por ejemplo, durante un rebalanceo de Kafka).
/// </summary>
/// <remarks>
/// Es transitoria: al reintentar se relee la guía y el inbox evita el doble efecto.
/// </remarks>
/// <param name="numeroGuia">Guía en la que ocurrió el conflicto.</param>
public class ConflictoConcurrenciaException(string numeroGuia)
    : Exception($"La guía {numeroGuia} cambió durante el procesamiento.");

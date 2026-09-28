namespace TccEventos.Aplicacion.Puertos;

/// Otra instancia modificó la guía entre la lectura y la escritura (p. ej. durante un rebalanceo).
/// Es transitoria: al reintentar se relee la guía y el inbox evita el doble efecto.
public class ConflictoConcurrenciaException(string numeroGuia)
    : Exception($"La guía {numeroGuia} cambió durante el procesamiento.");

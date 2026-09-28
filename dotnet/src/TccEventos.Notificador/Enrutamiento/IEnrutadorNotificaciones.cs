using TccEventos.Contratos.V1;

namespace TccEventos.Notificador.Enrutamiento;

/// Mueve una notificación a su siguiente etapa. Solo termina con éxito si el mensaje quedó guardado:
/// de lo contrario el offset del mensaje actual no puede avanzar.
public interface IEnrutadorNotificaciones
{
    /// Publica en la etapa de reintento correspondiente a pendiente.Intento (1 = primera etapa).
    Task ProgramarReintentoAsync(NotificacionPendienteV1 pendiente, CancellationToken ct);

    Task EnviarADlqAsync(string clave, string? contenido, string motivo, CancellationToken ct);
}

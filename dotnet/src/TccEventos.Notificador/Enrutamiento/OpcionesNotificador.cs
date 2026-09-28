namespace TccEventos.Notificador.Enrutamiento;

public class EtapaReintento
{
    public string Topico { get; init; } = "";
    public TimeSpan Espera { get; init; }
}

public class OpcionesNotificador
{
    public string GrupoConsumo { get; init; } = "notificador";
    public string TopicoDlq { get; init; } = "notificaciones.dlq";

    /// Escalera de reintentos no bloqueantes; el orden importa (intento 1 → primera etapa).
    public List<EtapaReintento> Reintentos { get; init; } = [];

    /// Usar solo las primeras N etapas (vacío = todas). Una lista de configuración no se puede acortar
    /// sobrescribiéndola por variables de entorno; esto sí (p. ej. Aiven gratuito solo admite 5 tópicos).
    public int? EtapasActivas { get; init; }

    public OpcionesNotificador ConEtapasActivas() => EtapasActivas is { } n
        ? new OpcionesNotificador { GrupoConsumo = GrupoConsumo, TopicoDlq = TopicoDlq, Reintentos = Reintentos.Take(n).ToList() }
        : this;
}

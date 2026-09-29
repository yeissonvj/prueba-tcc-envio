namespace TccEventos.Notificador.Enrutamiento;

/// <summary>Una etapa de la escalera de reintentos.</summary>
public class EtapaReintento
{
    /// <summary>Tópico de la etapa (por ejemplo notificaciones.reintento.1m).</summary>
    public string Topico { get; init; } = "";

    /// <summary>Cuánto esperar antes de reintentar en esta etapa.</summary>
    public TimeSpan Espera { get; init; }
}

/// <summary>Configuración del notificador (sección "Notificador").</summary>
public class OpcionesNotificador
{
    /// <summary>Grupo de consumo base; cada etapa de reintento usa uno derivado.</summary>
    public string GrupoConsumo { get; init; } = "notificador";

    /// <summary>Tópico de la DLQ de notificaciones.</summary>
    public string TopicoDlq { get; init; } = "notificaciones.dlq";

    /// <summary>Escalera de reintentos no bloqueantes; el orden importa (intento 1 → primera etapa).</summary>
    public List<EtapaReintento> Reintentos { get; init; } = [];

    /// <summary>
    /// Usar solo las primeras N etapas (vacío = todas). Una lista de configuración no se puede acortar
    /// sobrescribiéndola por variables de entorno; esto sí (p. ej. Aiven gratuito solo admite 5 tópicos).
    /// </summary>
    public int? EtapasActivas { get; init; }

    /// <summary>Devuelve las opciones recortadas a <see cref="EtapasActivas"/>, si está configurado.</summary>
    /// <returns>Una copia con solo las primeras etapas, o las mismas opciones si no hay límite.</returns>
    public OpcionesNotificador ConEtapasActivas() => EtapasActivas is { } n
        ? new OpcionesNotificador { GrupoConsumo = GrupoConsumo, TopicoDlq = TopicoDlq, Reintentos = Reintentos.Take(n).ToList() }
        : this;
}

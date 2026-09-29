namespace TccEventos.Procesador.Consumo;

/// <summary>Configuración del consumidor del procesador (sección "Consumidor").</summary>
public class OpcionesConsumidor
{
    /// <summary>Grupo de consumo de Kafka.</summary>
    public string GrupoConsumo { get; init; } = "procesador-estado";

    /// <summary>
    /// Intentos ante un error que no es de infraestructura (p. ej. un bug) antes de enviar a la DLQ,
    /// para que no congele la partición para siempre.
    /// </summary>
    public int IntentosErrorInesperado { get; init; } = 3;

    /// <summary>Primera espera del backoff exponencial de los reintentos bloqueantes: 200 ms, 400 ms, 800 ms...</summary>
    public int EsperaMinimaMs { get; init; } = 200;

    /// <summary>Tope de la espera entre reintentos (30 s).</summary>
    public int EsperaMaximaMs { get; init; } = 30_000;
}

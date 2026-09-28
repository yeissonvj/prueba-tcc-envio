namespace TccEventos.Procesador.Consumo;

public class OpcionesConsumidor
{
    public string GrupoConsumo { get; init; } = "procesador-estado";

    /// Un error que no es de infraestructura (p. ej. un bug) se reintenta pocas veces y va a la DLQ,
    /// para que no congele la partición para siempre.
    public int IntentosErrorInesperado { get; init; } = 3;

    /// Backoff exponencial de los reintentos bloqueantes: 200 ms, 400 ms, 800 ms... hasta 30 s.
    public int EsperaMinimaMs { get; init; } = 200;
    public int EsperaMaximaMs { get; init; } = 30_000;
}

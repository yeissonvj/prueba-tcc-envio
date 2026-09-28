namespace TccEventos.Api.PruebasIntegracion.Falsos;

/// Reloj que solo avanza cuando la prueba lo pide: permite probar tiempos (circuito abierto 15 s) sin esperar.
public class RelojManual(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
}

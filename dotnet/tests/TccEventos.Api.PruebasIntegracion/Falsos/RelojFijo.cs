namespace TccEventos.Api.PruebasIntegracion.Falsos;

public class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}

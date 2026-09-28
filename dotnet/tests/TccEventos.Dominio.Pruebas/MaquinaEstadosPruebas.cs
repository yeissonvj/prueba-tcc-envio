using TccEventos.Dominio;

namespace TccEventos.Dominio.Pruebas;

public class MaquinaEstadosPruebas
{
    [Theory]
    [InlineData(EstadoGuia.Creada, EstadoGuia.Recogida)]
    [InlineData(EstadoGuia.EnTransito, EstadoGuia.EnBodegaDestino)]
    [InlineData(EstadoGuia.EnReparto, EstadoGuia.Entregada)]
    [InlineData(EstadoGuia.EnReparto, EstadoGuia.Novedad)]
    [InlineData(EstadoGuia.Novedad, EstadoGuia.ReintentoEntrega)]
    [InlineData(EstadoGuia.ReintentoEntrega, EstadoGuia.EnReparto)]
    public void Permite_transiciones_validas(EstadoGuia desde, EstadoGuia hacia) =>
        Assert.True(MaquinaEstados.PuedeTransitar(desde, hacia));

    [Theory]
    [InlineData(EstadoGuia.Creada, EstadoGuia.Entregada)]
    [InlineData(EstadoGuia.Entregada, EstadoGuia.EnTransito)]
    [InlineData(EstadoGuia.Devuelta, EstadoGuia.EnReparto)]
    [InlineData(EstadoGuia.EnReparto, EstadoGuia.EnReparto)]
    public void Rechaza_transiciones_invalidas(EstadoGuia desde, EstadoGuia hacia) =>
        Assert.False(MaquinaEstados.PuedeTransitar(desde, hacia));

    [Theory]
    [InlineData(EstadoGuia.Entregada)]
    [InlineData(EstadoGuia.Devuelta)]
    public void Entregada_y_devuelta_son_estados_finales(EstadoGuia estado) =>
        Assert.True(MaquinaEstados.EsEstadoFinal(estado));

    [Fact]
    public void Todos_los_estados_tienen_reglas_definidas()
    {
        foreach (var estado in Enum.GetValues<EstadoGuia>())
            _ = MaquinaEstados.PuedeTransitar(estado, EstadoGuia.Creada);
    }
}
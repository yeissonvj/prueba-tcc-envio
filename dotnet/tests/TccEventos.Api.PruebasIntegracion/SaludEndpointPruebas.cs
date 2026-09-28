using System.Net;

namespace TccEventos.Api.PruebasIntegracion;

public class SaludEndpointPruebas : IClassFixture<FabricaApi>
{
    private readonly FabricaApi _fabrica;
    private readonly HttpClient _cliente;

    public SaludEndpointPruebas(FabricaApi fabrica)
    {
        _fabrica = fabrica;
        _fabrica.SondaKafka.Disponible = true;
        _fabrica.SondaContingencia.Disponible = true;
        _fabrica.SondaFiltro.Disponible = true;
        _cliente = fabrica.CreateClient();
    }

    [Fact]
    public async Task La_sonda_de_vida_no_depende_de_servicios_externos()
    {
        _fabrica.SondaKafka.Disponible = false;
        _fabrica.SondaContingencia.Disponible = false;

        var respuesta = await _cliente.GetAsync("/salud/viva");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task Con_todo_disponible_esta_lista()
    {
        var respuesta = await _cliente.GetAsync("/salud/lista");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Con_kafka_caido_y_contingencia_disponible_sigue_recibiendo_trafico()
    {
        _fabrica.SondaKafka.Disponible = false;

        var respuesta = await _cliente.GetAsync("/salud/lista");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Degraded", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sin_almacenamiento_durable_sale_del_balanceador_sin_revelar_que_fallo()
    {
        _fabrica.SondaKafka.Disponible = false;
        _fabrica.SondaContingencia.Disponible = false;

        var respuesta = await _cliente.GetAsync("/salud/lista");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("Unhealthy", await respuesta.Content.ReadAsStringAsync());
    }
}

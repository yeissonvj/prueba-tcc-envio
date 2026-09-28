using System.Net;
using System.Net.Http.Json;
using TccEventos.Contratos.V1;

namespace TccEventos.Api.PruebasIntegracion;

public class GuiasEndpointPruebas(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static readonly DateTimeOffset Hora = new(2026, 9, 26, 10, 15, 0, TimeSpan.FromHours(-5));

    private HttpClient ClientePortal() =>
        fabrica.CreateClient().Autenticar(EmisorTokensPruebas.Token(cliente: "portal-consulta", alcance: "guias:leer"));

    private GuiaV1 RegistrarGuia(string numero)
    {
        var guia = new GuiaV1(numero, EstadosV1.EnReparto, Hora, 2,
        [
            new(Guid.NewGuid(), EstadosV1.EnReparto, Hora, "TRANSPORTE", null, "APLICADO"),
            new(Guid.NewGuid(), EstadosV1.EnBodegaDestino, Hora.AddHours(-2), "TMS", null, "APLICADO")
        ]);
        fabrica.ConsultaGuias.Guias[numero] = guia;
        return guia;
    }

    [Fact]
    public async Task Con_alcance_de_lectura_devuelve_estado_e_historial_sin_cache()
    {
        var guia = RegistrarGuia("TCC700000001");

        var respuesta = await ClientePortal().GetAsync("/api/v1/guias/TCC700000001");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString());
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<GuiaV1>();
        Assert.Equal(guia.EstadoActual, cuerpo!.EstadoActual);
        Assert.Equal(guia.Historial.Select(e => e.IdEvento), cuerpo.Historial.Select(e => e.IdEvento));
    }

    [Fact]
    public async Task Una_guia_sin_eventos_procesados_responde_404()
    {
        var respuesta = await ClientePortal().GetAsync("/api/v1/guias/TCC799999999");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Un_numero_mal_formado_responde_400_sin_consultar_la_base()
    {
        var consultasAntes = fabrica.ConsultaGuias.Consultas;

        var respuesta = await ClientePortal().GetAsync("/api/v1/guias/TCC'%20OR%201=1");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(consultasAntes, fabrica.ConsultaGuias.Consultas);
    }

    [Fact]
    public async Task Un_token_de_solo_escritura_no_puede_consultar()
    {
        RegistrarGuia("TCC700000002");

        var respuesta = await fabrica.ClienteTms().GetAsync("/api/v1/guias/TCC700000002");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sin_token_responde_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().GetAsync("/api/v1/guias/TCC1")).StatusCode);
}

using System.Net.Http.Json;
using System.Text.Json;

namespace TccEventos.Api.PruebasIntegracion;

/// Prueba de contrato: si alguien cambia sin querer la ruta, los códigos de respuesta o los nombres
/// de los campos, falla aquí antes de romper a TMS y demás emisores. Es también la referencia
/// que la implementación Java debe reproducir.
public class ContratoOpenApiPruebas(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private async Task<JsonElement> Documento() =>
        await fabrica.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

    [Fact]
    public async Task Publica_la_ruta_de_ingesta_con_todas_sus_respuestas()
    {
        var respuestas = (await Documento())
            .GetProperty("paths").GetProperty("/api/v1/eventos-guia")
            .GetProperty("post").GetProperty("responses");

        var codigos = respuestas.EnumerateObject().Select(r => r.Name).Order();
        Assert.Equal(["200", "202", "400", "401", "403", "413", "429", "503"], codigos);
    }

    [Fact]
    public async Task Publica_la_consulta_de_guias_con_todas_sus_respuestas()
    {
        var respuestas = (await Documento())
            .GetProperty("paths").GetProperty("/api/v1/guias/{numeroGuia}")
            .GetProperty("get").GetProperty("responses");

        var codigos = respuestas.EnumerateObject().Select(r => r.Name).Order();
        Assert.Equal(["200", "400", "401", "403", "404", "429"], codigos);
    }

    [Fact]
    public async Task La_guia_V1_mantiene_su_forma()
    {
        var esquemas = (await Documento()).GetProperty("components").GetProperty("schemas");

        Assert.Equal(["estadoActual", "historial", "numeroGuia", "ultimoEventoEn", "version"],
            esquemas.GetProperty("GuiaV1").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["estado", "idEvento", "novedad", "ocurridoEn", "origen", "resultado"],
            esquemas.GetProperty("EventoHistorialV1").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task El_evento_V1_mantiene_sus_campos_en_camel_case()
    {
        var propiedades = (await Documento())
            .GetProperty("components").GetProperty("schemas").GetProperty("EventoGuiaV1")
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();

        Assert.Equal(["estado", "idEvento", "novedad", "numeroGuia", "ocurridoEn", "origen"], propiedades);
    }

    [Fact]
    public async Task La_respuesta_de_recepcion_mantiene_su_forma()
    {
        var propiedades = (await Documento())
            .GetProperty("components").GetProperty("schemas").GetProperty("RespuestaRecepcionV1")
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();

        Assert.Equal(["idEvento", "resultado"], propiedades);
    }
}

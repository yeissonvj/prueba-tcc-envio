using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TccEventos.Contratos.V1;

namespace TccEventos.Api.PruebasIntegracion;

public class EventosGuiaEndpointPruebas : IClassFixture<FabricaApi>
{
    private const string Ruta = "/api/v1/eventos-guia";

    private readonly FabricaApi _fabrica;
    private readonly HttpClient _cliente;

    public EventosGuiaEndpointPruebas(FabricaApi fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Publicador.Fallar = false;
        _cliente = fabrica.ClienteTms();
    }

    private static EventoGuiaV1 EventoValido() =>
        new(Guid.NewGuid(), "TCC123456789", EstadosV1.EnReparto, DateTimeOffset.UtcNow.AddMinutes(-1), "TMS");

    [Fact]
    public async Task Un_evento_valido_responde_202_con_ubicacion_y_se_publica()
    {
        var evento = EventoValido();

        var respuesta = await _cliente.PostAsJsonAsync(Ruta, evento);

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        Assert.Equal($"/api/v1/guias/{evento.NumeroGuia}", respuesta.Headers.Location?.OriginalString);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaRecepcionV1>();
        Assert.Equal(new RespuestaRecepcionV1(evento.IdEvento, RespuestaRecepcionV1.Aceptado), cuerpo);
        Assert.Contains(_fabrica.Publicador.Publicados, e => e.IdEvento == evento.IdEvento);
    }

    [Fact]
    public async Task Un_evento_repetido_responde_200_duplicado_y_no_se_publica_otra_vez()
    {
        var evento = EventoValido();
        await _cliente.PostAsJsonAsync(Ruta, evento);

        var respuesta = await _cliente.PostAsJsonAsync(Ruta, evento);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaRecepcionV1>();
        Assert.Equal(RespuestaRecepcionV1.Duplicado, cuerpo!.Resultado);
        Assert.Single(_fabrica.Publicador.Publicados, e => e.IdEvento == evento.IdEvento);
    }

    [Fact]
    public async Task Un_evento_que_no_cumple_el_contrato_responde_400_con_los_campos_en_error()
    {
        var respuesta = await _cliente.PostAsJsonAsync(Ruta, EventoValido() with { NumeroGuia = "TCC-1", Estado = "PERDIDA" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var errores = (await LeerJson(respuesta)).GetProperty("errors");
        Assert.True(errores.TryGetProperty("numeroGuia", out _));
        Assert.True(errores.TryGetProperty("estado", out _));
    }

    [Theory]
    [InlineData("{ \"idEvento\":")]
    [InlineData("{ \"idEvento\": 123 }")]
    public async Task Un_json_mal_formado_responde_400_y_no_500(string cuerpo)
    {
        var respuesta = await _cliente.PostAsync(Ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Si_nada_es_durable_responde_503_con_retry_after_sin_detalles_internos()
    {
        _fabrica.Publicador.Fallar = true;

        var respuesta = await _cliente.PostAsJsonAsync(Ruta, EventoValido());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), respuesta.Headers.RetryAfter?.Delta);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Kafka", texto);
    }

    // El límite de 64 KB del cuerpo lo aplica Kestrel (appsettings: Kestrel:Limits), que el
    // servidor en memoria no usa; aquí se prueba el límite de la aplicación sobre el campo libre.
    [Fact]
    public async Task Una_novedad_demasiado_larga_se_rechaza_con_400()
    {
        var evento = EventoValido() with { Estado = EstadosV1.Novedad, Novedad = new string('x', 5_000) };

        var respuesta = await _cliente.PostAsJsonAsync(Ruta, evento);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.DoesNotContain(_fabrica.Publicador.Publicados, e => e.IdEvento == evento.IdEvento);
    }

    private static async Task<JsonElement> LeerJson(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>());
}

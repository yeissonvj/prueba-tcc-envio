using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using TccEventos.Contratos.V1;

namespace TccEventos.Api.PruebasIntegracion;

public class SeguridadEndpointPruebas(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private const string Ruta = "/api/v1/eventos-guia";

    private static EventoGuiaV1 Evento(string origen = "TMS") =>
        new(Guid.NewGuid(), "TCC123456789", EstadosV1.EnReparto, DateTimeOffset.UtcNow.AddMinutes(-1), origen);

    private Task<HttpResponseMessage> Enviar(string? token, EventoGuiaV1? evento = null)
    {
        var cliente = fabrica.CreateClient();
        if (token is not null) cliente.Autenticar(token);
        return cliente.PostAsJsonAsync(Ruta, evento ?? Evento());
    }

    [Fact]
    public async Task Con_token_valido_y_origen_propio_se_acepta() =>
        Assert.Equal(HttpStatusCode.Accepted, (await Enviar(EmisorTokensPruebas.Token())).StatusCode);

    [Fact]
    public async Task Sin_token_responde_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(token: null)).StatusCode);

    public static TheoryData<string, string> TokensInvalidos => new()
    {
        { "firmado con otra llave", EmisorTokensPruebas.Token(llave: new RsaSecurityKey(RSA.Create(2048))) },
        { "emitido para otra API", EmisorTokensPruebas.Token(audiencia: "api-facturacion") },
        { "de otro emisor", EmisorTokensPruebas.Token(emisor: "https://emisor-falso/realms/tcc") },
        { "vencido", EmisorTokensPruebas.Token(vigencia: TimeSpan.FromMinutes(-2)) },
        { "firmado con HS256 (alg confusion)", TokenSimetrico() }
    };

    [Theory]
    [MemberData(nameof(TokensInvalidos))]
    public async Task Un_token_invalido_responde_401(string caso, string token)
    {
        var respuesta = await Enviar(token);

        Assert.True(respuesta.StatusCode == HttpStatusCode.Unauthorized, $"Caso '{caso}': {respuesta.StatusCode}");
    }

    [Fact]
    public async Task Un_token_sin_permiso_de_escritura_responde_403()
    {
        var respuesta = await Enviar(EmisorTokensPruebas.Token(cliente: "portal-consulta", alcance: "guias:leer"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_cliente_no_puede_reportar_eventos_de_otro_sistema()
    {
        var evento = Evento(origen: "TRANSPORTE");

        var respuesta = await Enviar(EmisorTokensPruebas.Token(cliente: "tms"), evento);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.DoesNotContain(fabrica.Publicador.Publicados, e => e.IdEvento == evento.IdEvento);
    }

    [Fact]
    public async Task Un_cliente_desconocido_no_puede_escribir_aunque_tenga_el_alcance()
    {
        var respuesta = await Enviar(EmisorTokensPruebas.Token(cliente: "sistema-nuevo"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Las_sondas_de_salud_no_requieren_token() =>
        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().GetAsync("/salud/viva")).StatusCode);

    private static string TokenSimetrico()
    {
        var llave = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        return new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = EmisorTokensPruebas.Emisor,
            Audience = EmisorTokensPruebas.Audiencia,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["azp"] = "tms", ["scope"] = "eventos:escribir" },
            SigningCredentials = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256)
        });
    }
}

public class LimitePeticionesPruebas(FabricaApiLimiteBajo fabrica) : IClassFixture<FabricaApiLimiteBajo>
{
    [Fact]
    public async Task Al_superar_el_limite_responde_429_con_retry_after_sin_afectar_a_otros_clientes()
    {
        var tms = fabrica.CreateClient().Autenticar(EmisorTokensPruebas.Token(cliente: "tms"));
        var transporte = fabrica.CreateClient().Autenticar(EmisorTokensPruebas.Token(cliente: "transporte"));
        HttpResponseMessage? ultima = null;

        for (var i = 0; i < 5; i++)
            ultima = await tms.PostAsJsonAsync("/api/v1/eventos-guia", Evento("TMS"));
        var otroCliente = await transporte.PostAsJsonAsync("/api/v1/eventos-guia", Evento("TRANSPORTE"));

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima!.StatusCode);
        Assert.NotNull(ultima.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.Accepted, otroCliente.StatusCode);
    }

    private static EventoGuiaV1 Evento(string origen) =>
        new(Guid.NewGuid(), "TCC123456789", EstadosV1.EnReparto, DateTimeOffset.UtcNow.AddMinutes(-1), origen);
}

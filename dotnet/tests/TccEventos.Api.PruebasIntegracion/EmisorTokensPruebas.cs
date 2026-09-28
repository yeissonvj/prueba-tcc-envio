using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TccEventos.Api.PruebasIntegracion;

/// Hace las veces de Keycloak en las pruebas: firma tokens con una llave RSA generada en memoria.
/// La API los valida con las mismas reglas que en producción (emisor, audiencia, vigencia, RS256).
public static class EmisorTokensPruebas
{
    public const string Emisor = "https://emisor-pruebas.tcc.local/realms/tcc";
    public const string Audiencia = "api-ingesta";

    public static readonly RsaSecurityKey Llave = new(RSA.Create(2048)) { KeyId = "llave-pruebas" };

    public static string Token(
        string cliente = "tms",
        string alcance = "eventos:escribir",
        string audiencia = Audiencia,
        string emisor = Emisor,
        TimeSpan? vigencia = null,
        SecurityKey? llave = null)
    {
        var ahora = DateTime.UtcNow;
        var expira = ahora + (vigencia ?? TimeSpan.FromMinutes(5));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            IssuedAt = expira < ahora ? expira.AddMinutes(-5) : ahora,
            NotBefore = expira < ahora ? expira.AddMinutes(-5) : ahora,
            Expires = expira,
            Claims = new Dictionary<string, object> { ["azp"] = cliente, ["scope"] = alcance },
            SigningCredentials = new SigningCredentials(llave ?? Llave, SecurityAlgorithms.RsaSha256)
        });
    }

    public static HttpClient Autenticar(this HttpClient cliente, string token)
    {
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }
}

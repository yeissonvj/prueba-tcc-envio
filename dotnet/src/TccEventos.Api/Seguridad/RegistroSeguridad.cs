using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace TccEventos.Api.Seguridad;

public static class RegistroSeguridad
{
    public static IServiceCollection AgregarSeguridad(this IServiceCollection services, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection("Seguridad").Get<OpcionesSeguridad>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Seguridad'.");
        if (string.IsNullOrWhiteSpace(opciones.Emisor))
            throw new InvalidOperationException("Falta la configuración 'Seguridad:Emisor'.");

        services.AddSingleton(opciones);
        services.AddSingleton<AutorizadorOrigen>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = opciones.Emisor;          // emisor válido + llaves públicas (JWKS) del IdP
                jwt.Audience = opciones.Audiencia;        // rechaza tokens emitidos para otras APIs
                jwt.RequireHttpsMetadata = opciones.RequiereHttps;
                jwt.MapInboundClaims = false;             // claims con su nombre real: azp, scope
                jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256]; // sin "alg confusion"
                jwt.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

                if (!string.IsNullOrWhiteSpace(opciones.DireccionInterna))
                {
                    // Llaves por la red interna; el emisor esperado en el token sigue siendo el público.
                    jwt.MetadataAddress = $"{opciones.DireccionInterna.TrimEnd('/')}/.well-known/openid-configuration";
                    jwt.TokenValidationParameters.ValidIssuer = opciones.Emisor;
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.EscribirEventos, politica => politica
                .RequireAuthenticatedUser()
                .RequireAssertion(contexto => TieneAlcance(contexto.User, Alcances.EscribirEventos)))
            .AddPolicy(Politicas.LeerGuias, politica => politica
                .RequireAuthenticatedUser()
                .RequireAssertion(contexto => TieneAlcance(contexto.User, Alcances.LeerGuias)));

        services.AddRateLimiter(limitador =>
        {
            // Un cubo de fichas por cliente: si TMS entra en bucle, solo TMS recibe 429.
            // Es por instancia; el límite global lo aplica el API Gateway.
            limitador.AddPolicy(Politicas.LimitePorCliente, http => RateLimitPartition.GetTokenBucketLimiter(
                http.User.FindFirst(Reclamos.Cliente)?.Value ?? "anonimo",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = opciones.RafagaPorCliente,
                    TokensPerPeriod = opciones.PeticionesPorSegundoPorCliente,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

            limitador.OnRejected = async (contexto, ct) =>
            {
                var http = contexto.HttpContext;
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
                    ? Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds))
                    : 1;

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = segundos.ToString();

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiadas peticiones",
                        Detail = "Se superó el límite de peticiones del cliente. Reintente tras Retry-After."
                    }
                });
            };
        });

        return services;
    }

    public static WebApplication UsarSeguridad(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter(); // después de autenticar: el límite se parte por cliente
        return app;
    }

    private static bool TieneAlcance(System.Security.Claims.ClaimsPrincipal usuario, string alcance) =>
        usuario.FindFirst(Reclamos.Alcances)?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(alcance, StringComparer.Ordinal) ?? false;
}

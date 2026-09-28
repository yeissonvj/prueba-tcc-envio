using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TccEventos.Api.PruebasIntegracion.Falsos;
using TccEventos.Api.Salud;
using TccEventos.Api.Trabajos;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Consultas;

namespace TccEventos.Api.PruebasIntegracion;

/// Levanta la API completa en memoria (rutas, validación, seguridad, errores, JSON, salud, OpenAPI)
/// con Kafka, Redis, PostgreSQL y Keycloak reemplazados por falsos. Las pruebas contra Docker real van en el Paso 8.
public class FabricaApi : WebApplicationFactory<Program>
{
    public PublicadorEnMemoria Publicador { get; } = new();
    public FiltroDuplicadosEnMemoria Filtro { get; } = new();
    public SondaFalsa SondaKafka { get; } = new();
    public SondaFalsa SondaContingencia { get; } = new();
    public SondaFalsa SondaFiltro { get; } = new();
    public ConsultaGuiasEnMemoria ConsultaGuias { get; } = new();

    protected virtual int PeticionesPorSegundoPorCliente => 1000;

    /// Cliente HTTP con un token válido de TMS (alcance eventos:escribir).
    public HttpClient ClienteTms() => CreateClient().Autenticar(EmisorTokensPruebas.Token());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.UseSetting("Kafka:Servidores", "no-se-usa:9092");
        builder.UseSetting("Redis:Conexion", "no-se-usa:6379");
        builder.UseSetting("Postgres:Conexion", "Host=no-se-usa");
        builder.UseSetting("Documentacion:Habilitada", "true");
        builder.UseSetting("Seguridad:Emisor", EmisorTokensPruebas.Emisor);
        builder.UseSetting("Seguridad:PeticionesPorSegundoPorCliente", PeticionesPorSegundoPorCliente.ToString());
        builder.UseSetting("Seguridad:RafagaPorCliente", PeticionesPorSegundoPorCliente.ToString());

        builder.ConfigureTestServices(services =>
        {
            // El relay real intentaría conectarse a PostgreSQL cada 5 s; aquí no aplica.
            var relay = services.Single(d => d.ImplementationType == typeof(ServicioRelayContingencia));
            services.Remove(relay);

            services.AddSingleton<IPublicadorEventos>(Publicador);
            services.AddSingleton<IFiltroDuplicados>(Filtro);
            services.AddSingleton<IConsultaGuias>(ConsultaGuias);

            services.AddKeyedSingleton<ISonda>(RegistroSalud.SondaKafka, SondaKafka);
            services.AddKeyedSingleton<ISonda>(RegistroSalud.SondaContingencia, SondaContingencia);
            services.AddKeyedSingleton<ISonda>(RegistroSalud.SondaFiltro, SondaFiltro);

            // En vez de descargar la configuración del IdP, se entrega la del emisor de pruebas.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
            {
                var configuracion = new OpenIdConnectConfiguration { Issuer = EmisorTokensPruebas.Emisor };
                configuracion.SigningKeys.Add(EmisorTokensPruebas.Llave);
                jwt.Configuration = configuracion;
                jwt.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuracion);
                jwt.TokenValidationParameters.ValidIssuer = EmisorTokensPruebas.Emisor;
                jwt.TokenValidationParameters.IssuerSigningKey = EmisorTokensPruebas.Llave;
            });
        });
    }
}

/// Misma API con un límite de 3 peticiones por cliente, para probar el 429.
public class FabricaApiLimiteBajo : FabricaApi
{
    protected override int PeticionesPorSegundoPorCliente => 3;
}

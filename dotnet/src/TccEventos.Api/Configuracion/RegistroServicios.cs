using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TccEventos.Api.Trabajos;
using TccEventos.Api.Validacion;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Decoradores;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Consultas;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Postgres;
using TccEventos.Infraestructura.Redis;

namespace TccEventos.Api.Configuracion;

/// Raíz de composición: el único lugar donde se decide qué adaptador implementa cada puerto
/// y en qué orden se envuelven los decoradores.
public static class RegistroServicios
{
    public static IServiceCollection AgregarIngesta(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ValidadorEventoGuiaV1>();

        services.AgregarPublicacion(configuracion);
        services.AgregarFiltroDuplicados(configuracion);

        services.AddSingleton<RecibirEvento>();

        return services;
    }

    /// Prepara al arrancar lo que es perezoso: productor de Kafka, conexión a Redis y la configuración
    /// OIDC + llaves públicas del emisor de tokens. Sin esto, la primera petición autenticada pagaba ~2,3 s.
    /// Si el emisor no responde, no se impide el arranque: se reintentará en la primera petición.
    public static async Task<WebApplication> CalentarDependenciasAsync(this WebApplication app)
    {
        app.Services.GetRequiredService<IPublicadorEventos>();
        app.Services.GetRequiredService<IFiltroDuplicados>();

        var jwt = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        if (jwt.ConfigurationManager is { } configuracionOidc)
        {
            try
            {
                using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await configuracionOidc.GetConfigurationAsync(limite.Token);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "No se pudo precargar la configuración del emisor de tokens; se intentará en la primera petición");
            }
        }

        return app;
    }

    /// RecibirEvento → Contingencia( Circuito( Kafka ) ); el relay usa Circuito( Kafka ) directo.
    private static void AgregarPublicacion(this IServiceCollection services, IConfiguration configuracion)
    {
        var kafka = LeerSeccion<OpcionesKafka>(configuracion, "Kafka");
        Exigir(kafka.Servidores, "Kafka:Servidores");

        var postgres = LeerSeccion<OpcionesPostgres>(configuracion, "Postgres");
        Exigir(postgres.Conexion, "Postgres:Conexion");

        services.AddSingleton(kafka);
        services.AddSingleton<ProductorKafka>();
        services.AddSingleton<PublicadorKafka>();
        services.AddSingleton(sp => new PublicadorConCircuito(
            sp.GetRequiredService<PublicadorKafka>(),
            kafka,
            sp.GetRequiredService<ILogger<PublicadorConCircuito>>()));

        services.AddSingleton(_ => FabricaBaseDatos.Crear(postgres));
        services.AddSingleton<IConsultaGuias, ConsultaGuiasPostgres>();
        services.AddSingleton<IAlmacenContingencia, AlmacenContingenciaPostgres>();

        services.AddSingleton<IPublicadorEventos>(sp => new PublicadorConContingencia(
            sp.GetRequiredService<PublicadorConCircuito>(),
            sp.GetRequiredService<IAlmacenContingencia>(),
            sp.GetRequiredService<ILogger<PublicadorConContingencia>>()));

        services.AddSingleton(sp => new ReenviarContingencia(
            sp.GetRequiredService<IAlmacenContingencia>(),
            sp.GetRequiredService<PublicadorConCircuito>()));
        services.AddHostedService<ServicioRelayContingencia>();
    }

    /// IFiltroDuplicados → Tolerante( Redis ).
    private static void AgregarFiltroDuplicados(this IServiceCollection services, IConfiguration configuracion)
    {
        var redis = LeerSeccion<OpcionesRedis>(configuracion, "Redis");
        Exigir(redis.Conexion, "Redis:Conexion");

        services.AddSingleton(redis);
        services.AddSingleton<IConnectionMultiplexer>(_ => ConectarRedis(redis));
        services.AddSingleton<FiltroDuplicadosRedis>();
        services.AddSingleton<IFiltroDuplicados>(sp => new FiltroDuplicadosTolerante(
            sp.GetRequiredService<FiltroDuplicadosRedis>(),
            sp.GetRequiredService<ILogger<FiltroDuplicadosTolerante>>()));
    }

    private static T LeerSeccion<T>(IConfiguration configuracion, string seccion) =>
        configuracion.GetSection(seccion).Get<T>()
        ?? throw new InvalidOperationException($"Falta la sección de configuración '{seccion}'.");

    private static void Exigir(string valor, string clave)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException($"Falta la configuración '{clave}'.");
    }

    private static IConnectionMultiplexer ConectarRedis(OpcionesRedis redis)
    {
        var opciones = ConfigurationOptions.Parse(redis.Conexion);
        opciones.BacklogPolicy = BacklogPolicy.FailFast;
        if (!string.IsNullOrEmpty(redis.Contrasena))
            opciones.Password = redis.Contrasena;

        return ConnectionMultiplexer.Connect(opciones);
    }
}

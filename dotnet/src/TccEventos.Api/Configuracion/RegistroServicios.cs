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

/// <summary>
/// Raíz de composición de la API: el único lugar donde se decide qué adaptador implementa cada puerto
/// y en qué orden se envuelven los decoradores.
/// </summary>
public static class RegistroServicios
{
    /// <summary>Registra la recepción de eventos: validador, publicación durable, filtro de duplicados y caso de uso.</summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <param name="configuracion">Configuración de la aplicación.</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    public static IServiceCollection AgregarIngesta(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ValidadorEventoGuiaV1>();

        services.AgregarPublicacion(configuracion);
        services.AgregarFiltroDuplicados(configuracion);

        services.AddSingleton<RecibirEvento>();

        return services;
    }

    /// <summary>
    /// Prepara al arrancar lo que es perezoso: productor de Kafka, conexión a Redis y la configuración
    /// OIDC + llaves públicas del emisor de tokens. Sin esto, la primera petición autenticada pagaba ~2,3 s.
    /// </summary>
    /// <remarks>Si el emisor no responde, no se impide el arranque: se reintentará en la primera petición.</remarks>
    /// <param name="app">Aplicación web ya construida.</param>
    /// <returns>La misma aplicación, para encadenar llamadas.</returns>
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

    /// <summary>
    /// Registra la publicación durable: RecibirEvento → Contingencia( Circuito( Kafka ) );
    /// el relay de contingencia usa Circuito( Kafka ) directo.
    /// </summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <param name="configuracion">Configuración de la aplicación (secciones Kafka y Postgres).</param>
    /// <exception cref="InvalidOperationException">Si falta la configuración de Kafka o PostgreSQL.</exception>
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

    /// <summary>Registra el filtro de duplicados: IFiltroDuplicados → Tolerante( Redis ).</summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <param name="configuracion">Configuración de la aplicación (sección Redis).</param>
    /// <exception cref="InvalidOperationException">Si falta la configuración de Redis.</exception>
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

    /// <summary>Lee y convierte una sección de configuración.</summary>
    /// <typeparam name="T">Tipo de las opciones.</typeparam>
    /// <param name="configuracion">Configuración de la aplicación.</param>
    /// <param name="seccion">Nombre de la sección.</param>
    /// <returns>Las opciones leídas.</returns>
    /// <exception cref="InvalidOperationException">Si la sección no existe.</exception>
    private static T LeerSeccion<T>(IConfiguration configuracion, string seccion) =>
        configuracion.GetSection(seccion).Get<T>()
        ?? throw new InvalidOperationException($"Falta la sección de configuración '{seccion}'.");

    /// <summary>Exige que un valor de configuración obligatorio no esté vacío; falla al arrancar si lo está.</summary>
    /// <param name="valor">Valor leído.</param>
    /// <param name="clave">Nombre de la clave, para el mensaje de error.</param>
    /// <exception cref="InvalidOperationException">Si el valor está vacío.</exception>
    private static void Exigir(string valor, string clave)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException($"Falta la configuración '{clave}'.");
    }

    /// <summary>
    /// Conecta con Redis en modo "fallar rápido": si Redis no está, los comandos fallan de inmediato
    /// en vez de quedar en cola (el filtro es una optimización y no puede frenar la recepción).
    /// </summary>
    /// <param name="redis">Opciones de conexión.</param>
    /// <returns>La conexión compartida.</returns>
    private static IConnectionMultiplexer ConectarRedis(OpcionesRedis redis)
    {
        var opciones = ConfigurationOptions.Parse(redis.Conexion);
        opciones.BacklogPolicy = BacklogPolicy.FailFast;
        if (!string.IsNullOrEmpty(redis.Contrasena))
            opciones.Password = redis.Contrasena;

        return ConnectionMultiplexer.Connect(opciones);
    }
}

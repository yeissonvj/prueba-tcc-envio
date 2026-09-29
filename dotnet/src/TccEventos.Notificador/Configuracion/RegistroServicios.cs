using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Notificaciones;
using TccEventos.Infraestructura.Postgres;
using TccEventos.Notificador.Enrutamiento;

namespace TccEventos.Notificador.Configuracion;

/// <summary>Raíz de composición del notificador.</summary>
public static class RegistroServicios
{
    /// <summary>
    /// Registra el registro de notificaciones, el directorio de contactos, los proveedores (cada uno con su
    /// circuito), el caso de uso, el enrutador y un consumidor para los cambios más uno por etapa de reintento.
    /// </summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <param name="configuracion">Configuración (secciones Kafka, Postgres, Proveedores y Notificador).</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    /// <exception cref="InvalidOperationException">Si falta configuración obligatoria o no hay etapas de reintento.</exception>
    public static IServiceCollection AgregarNotificador(this IServiceCollection services, IConfiguration configuracion)
    {
        var kafka = Leer<OpcionesKafka>(configuracion, "Kafka");
        if (string.IsNullOrWhiteSpace(kafka.Servidores))
            throw new InvalidOperationException("Falta la configuración 'Kafka:Servidores'.");
        var postgres = Leer<OpcionesPostgres>(configuracion, "Postgres");
        var proveedores = configuracion.GetSection("Proveedores").Get<OpcionesProveedores>() ?? new OpcionesProveedores();
        var notificador = Leer<OpcionesNotificador>(configuracion, "Notificador").ConEtapasActivas();
        if (notificador.Reintentos.Count == 0)
            throw new InvalidOperationException("Falta la configuración 'Notificador:Reintentos'.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(kafka);
        services.AddSingleton(proveedores);
        services.AddSingleton(notificador);
        services.AddSingleton(_ => FabricaBaseDatos.Crear(postgres));
        services.AddSingleton<ProductorKafka>();

        services.AddSingleton<IRegistroNotificaciones, RegistroNotificacionesPostgres>();
        services.AddSingleton<IDirectorioContactos, DirectorioContactosSimulado>();
        services.AddSingleton<IProveedorNotificacion>(sp => ConCircuito(sp, CanalNotificacion.Sms, proveedores.Sms));
        services.AddSingleton<IProveedorNotificacion>(sp => ConCircuito(sp, CanalNotificacion.Correo, proveedores.Correo));
        services.AddSingleton<NotificarCambioEstado>();

        services.AddSingleton<IEnrutadorNotificaciones, EnrutadorNotificacionesKafka>();
        services.AddSingleton<ManejadorNotificacion>();

        // Un consumidor para los cambios y uno por etapa de reintento, cada uno con su grupo.
        // AddSingleton<IHostedService>: AddHostedService descartaría en silencio las instancias repetidas.
        services.AddSingleton<IHostedService>(sp => Consumidor(sp,
            new SuscripcionKafka(kafka.TopicoEstadosCambiados, notificador.GrupoConsumo, ParticionesEnParalelo: true),
            sp.GetRequiredService<ManejadorNotificacion>().ManejarCambioAsync));

        foreach (var etapa in notificador.Reintentos)
        {
            services.AddSingleton<IHostedService>(sp => Consumidor(sp,
                new SuscripcionKafka(etapa.Topico, $"{notificador.GrupoConsumo}-{etapa.Topico}", EnrutadorNotificacionesKafka.Vencimiento),
                sp.GetRequiredService<ManejadorNotificacion>().ManejarReintentoAsync));
        }

        return services;
    }

    /// <summary>Crea un proveedor simulado envuelto en su propio circuito (si el SMS cae, el correo sigue).</summary>
    /// <param name="sp">Proveedor de servicios.</param>
    /// <param name="canal">Canal del proveedor.</param>
    /// <param name="opciones">Comportamiento simulado del proveedor.</param>
    /// <returns>El proveedor protegido por el circuito.</returns>
    private static IProveedorNotificacion ConCircuito(IServiceProvider sp, CanalNotificacion canal, OpcionesProveedorSimulado opciones) =>
        new ProveedorConCircuito(
            new ProveedorSimulado(canal, opciones, sp.GetRequiredService<ILogger<ProveedorSimulado>>()),
            sp.GetRequiredService<OpcionesProveedores>(),
            sp.GetRequiredService<ILogger<ProveedorConCircuito>>());

    /// <summary>Crea un consumidor de Kafka para una suscripción y un manejador.</summary>
    /// <param name="sp">Proveedor de servicios.</param>
    /// <param name="suscripcion">Tópico, grupo y forma de consumo.</param>
    /// <param name="manejar">Función que resuelve cada mensaje.</param>
    /// <returns>El consumidor, que corre como servicio en segundo plano.</returns>
    private static ConsumidorKafka Consumidor(IServiceProvider sp, SuscripcionKafka suscripcion, Func<MensajeKafka, CancellationToken, Task> manejar) =>
        new(sp.GetRequiredService<OpcionesKafka>(), suscripcion, manejar,
            sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>());

    /// <summary>Lee y convierte una sección de configuración obligatoria.</summary>
    /// <typeparam name="T">Tipo de las opciones.</typeparam>
    /// <param name="configuracion">Configuración de la aplicación.</param>
    /// <param name="seccion">Nombre de la sección.</param>
    /// <returns>Las opciones leídas.</returns>
    /// <exception cref="InvalidOperationException">Si la sección no existe.</exception>
    private static T Leer<T>(IConfiguration configuracion, string seccion) =>
        configuracion.GetSection(seccion).Get<T>()
        ?? throw new InvalidOperationException($"Falta la sección de configuración '{seccion}'.");
}

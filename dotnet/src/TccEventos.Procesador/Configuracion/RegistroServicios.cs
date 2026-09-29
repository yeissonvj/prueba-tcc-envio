using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Postgres;
using TccEventos.Procesador.Consumo;

namespace TccEventos.Procesador.Configuracion;

/// <summary>Raíz de composición del procesador de estado.</summary>
public static class RegistroServicios
{
    /// <summary>
    /// Registra el repositorio, el caso de uso, la DLQ, el consumidor de guias.eventos.recibidos
    /// (particiones en paralelo) y el relay de la bandeja de salida.
    /// </summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <param name="configuracion">Configuración (secciones Kafka, Postgres y Consumidor).</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    /// <exception cref="InvalidOperationException">Si falta la configuración de Kafka o PostgreSQL.</exception>
    public static IServiceCollection AgregarProcesador(this IServiceCollection services, IConfiguration configuracion)
    {
        var kafka = configuracion.GetSection("Kafka").Get<OpcionesKafka>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Kafka'.");
        if (string.IsNullOrWhiteSpace(kafka.Servidores))
            throw new InvalidOperationException("Falta la configuración 'Kafka:Servidores'.");

        var postgres = configuracion.GetSection("Postgres").Get<OpcionesPostgres>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Postgres'.");
        var consumidor = configuracion.GetSection("Consumidor").Get<OpcionesConsumidor>() ?? new OpcionesConsumidor();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(kafka);
        services.AddSingleton(consumidor);
        services.AddSingleton(_ => FabricaBaseDatos.Crear(postgres));
        services.AddSingleton<ProductorKafka>();

        services.AddSingleton<IRepositorioGuias, RepositorioGuiasPostgres>();
        services.AddSingleton<ProcesarEvento>();
        services.AddSingleton<IDestinoDlq, DestinoDlqKafka>();
        services.AddSingleton<ManejadorMensajeRecibido>();
        // AddSingleton<IHostedService> y no AddHostedService: este último ignora en silencio
        // una segunda instancia del mismo tipo (TryAddEnumerable).
        services.AddSingleton<IHostedService>(sp => new ConsumidorKafka(
            kafka,
            // Particiones en paralelo: el orden por guía se conserva (la guía es la clave de partición)
            // y la instancia procesa hasta 24 guías distintas a la vez.
            new SuscripcionKafka(kafka.TopicoEventosRecibidos, consumidor.GrupoConsumo, ParticionesEnParalelo: true),
            sp.GetRequiredService<ManejadorMensajeRecibido>().ManejarAsync,
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<RelayBandejaSalida>();
        services.AddHostedService<ServicioRelayBandejaSalida>();

        return services;
    }
}

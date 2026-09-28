using System.Text.RegularExpressions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace TccEventos.Procesador.PruebasIntegracion.Integracion;

/// PostgreSQL y Kafka desechables (Testcontainers), compartidos por las clases de la colección.
/// El esquema se crea con los MISMOS archivos de db/migraciones que usa Flyway: se prueba el esquema real.
public sealed partial class InfraestructuraReal : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.2").Build();

    public NpgsqlDataSource BaseDatos { get; private set; } = null!;
    public string ServidoresKafka => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());
        BaseDatos = NpgsqlDataSource.Create(_postgres.GetConnectionString());

        foreach (var archivo in Migraciones())
        {
            await using var comando = BaseDatos.CreateCommand(await File.ReadAllTextAsync(archivo));
            await comando.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await BaseDatos.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    /// Cada prueba empieza con las tablas vacías.
    public async Task LimpiarAsync()
    {
        await using var comando = BaseDatos.CreateCommand("""
            TRUNCATE guias, historial_eventos, bandeja_salida, contingencia_eventos, notificaciones_enviadas RESTART IDENTITY
            """);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<long> ContarAsync(string sql, params object[] parametros)
    {
        await using var comando = BaseDatos.CreateCommand(sql);
        foreach (var valor in parametros)
            comando.Parameters.Add(new NpgsqlParameter { Value = valor });
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    public async Task CrearTopicoAsync(string topico)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = ServidoresKafka }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topico, NumPartitions = 3, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    /// Lee todo lo que haya en el tópico (espera hasta que llegue la cantidad esperada o venza el tiempo).
    public List<ConsumeResult<string, string>> Leer(string topico, int esperados, TimeSpan limite)
    {
        using var consumidor = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = ServidoresKafka,
            GroupId = $"pruebas-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumidor.Subscribe(topico);

        var leidos = new List<ConsumeResult<string, string>>();
        var hasta = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < hasta && leidos.Count < esperados)
        {
            if (consumidor.Consume(TimeSpan.FromMilliseconds(500)) is { Message: not null } registro)
                leidos.Add(registro);
        }
        // Un poco más, para detectar duplicados que lleguen después de los esperados.
        var extra = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < extra)
        {
            if (consumidor.Consume(TimeSpan.FromMilliseconds(300)) is { Message: not null } registro)
                leidos.Add(registro);
        }
        consumidor.Close();
        return leidos;
    }

    private static IEnumerable<string> Migraciones()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !Directory.Exists(Path.Combine(carpeta.FullName, "db", "migraciones")))
            carpeta = carpeta.Parent;
        if (carpeta is null)
            throw new InvalidOperationException("No se encontró la carpeta db/migraciones del repositorio.");

        return Directory.GetFiles(Path.Combine(carpeta.FullName, "db", "migraciones"), "V*__*.sql")
            .OrderBy(archivo => int.Parse(VersionFlyway().Match(Path.GetFileName(archivo)).Groups[1].Value));
    }

    [GeneratedRegex(@"^V(\d+)__")]
    private static partial Regex VersionFlyway();
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionInfraestructura : ICollectionFixture<InfraestructuraReal>
{
    public const string Nombre = "Infraestructura real (Testcontainers)";
}

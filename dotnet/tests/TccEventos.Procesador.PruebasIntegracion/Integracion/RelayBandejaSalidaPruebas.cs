using System.Text.Json;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Postgres;

namespace TccEventos.Procesador.PruebasIntegracion.Integracion;

[Collection(ColeccionInfraestructura.Nombre)]
public class RelayBandejaSalidaPruebas(InfraestructuraReal infra) : IAsyncLifetime
{
    private static readonly DateTimeOffset Hora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() => infra.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dos_relays_en_paralelo_publican_cada_cambio_una_sola_vez_y_en_orden()
    {
        var topico = $"estados.pruebas.{Guid.NewGuid():N}";
        await infra.CrearTopicoAsync(topico);
        var opciones = new OpcionesKafka { Servidores = infra.ServidoresKafka, TopicoEstadosCambiados = topico };

        var procesar = new ProcesarEvento(new RepositorioGuiasPostgres(infra.BaseDatos));
        EstadoGuia[] recorrido = [EstadoGuia.Creada, EstadoGuia.Recogida, EstadoGuia.EnBodegaOrigen, EstadoGuia.EnTransito, EstadoGuia.EnBodegaDestino];
        for (var i = 0; i < recorrido.Length; i++)
            await procesar.EjecutarAsync(new EventoGuia(Guid.NewGuid(), "TCC20", recorrido[i], Hora.AddHours(i), "TMS"), default);

        using var productor = new ProductorKafka(opciones);
        var relayA = new RelayBandejaSalida(infra.BaseDatos, productor, opciones);
        var relayB = new RelayBandejaSalida(infra.BaseDatos, productor, opciones);

        var publicados = await Task.WhenAll(relayA.PublicarPendientesAsync(100, default), relayB.PublicarPendientesAsync(100, default));

        Assert.Equal(recorrido.Length, publicados.Sum()); // el candado evita que ambos publiquen lo mismo
        Assert.Equal(0, await infra.ContarAsync("SELECT count(*) FROM bandeja_salida"));

        var mensajes = infra.Leer(topico, recorrido.Length, TimeSpan.FromSeconds(20));
        var versiones = mensajes.Select(m => JsonSerializer.Deserialize<EstadoGuiaCambiadoV1>(m.Message.Value, Json)!.Version);
        Assert.Equal([1L, 2, 3, 4, 5], versiones);
        Assert.All(mensajes, m => Assert.Equal("TCC20", m.Message.Key));
    }
}

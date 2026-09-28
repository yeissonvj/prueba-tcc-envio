using Microsoft.Extensions.Logging.Abstractions;
using TccEventos.Api.PruebasIntegracion.Falsos;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Api.PruebasIntegracion;

public class PublicadorConCircuitoPruebas
{
    private static readonly OpcionesKafka Opciones = new()
    {
        Servidores = "no-se-usa:9092",
        CircuitoMinimoEnvios = 4,
        CircuitoVentanaSegundos = 10,
        CircuitoSegundosAbierto = 15
    };

    private readonly PublicadorEnMemoria _kafka = new() { Fallar = true };
    private readonly RelojManual _reloj = new(new DateTimeOffset(2026, 11, 30, 10, 0, 0, TimeSpan.Zero));
    private readonly PublicadorConCircuito _publicador;

    public PublicadorConCircuitoPruebas() =>
        _publicador = new(_kafka, Opciones, NullLogger<PublicadorConCircuito>.Instance, _reloj);

    private static EventoGuia NuevoEvento() =>
        new(Guid.NewGuid(), "TCC123", EstadoGuia.Recogida, DateTimeOffset.UnixEpoch, "TMS");

    private async Task FallarVeces(int veces)
    {
        for (var i = 0; i < veces; i++)
            await Assert.ThrowsAsync<PublicacionFallidaException>(
                () => _publicador.PublicarAsync(NuevoEvento(), CancellationToken.None));
    }

    [Fact]
    public async Task Tras_fallas_sostenidas_el_circuito_se_abre_y_ya_no_intenta_publicar()
    {
        await FallarVeces(Opciones.CircuitoMinimoEnvios);
        var intentosAlAbrir = _kafka.Intentos;

        await FallarVeces(5);

        Assert.Equal(intentosAlAbrir, _kafka.Intentos);
    }

    [Fact]
    public async Task Pasado_el_tiempo_abierto_prueba_de_nuevo_y_si_funciona_se_cierra()
    {
        await FallarVeces(Opciones.CircuitoMinimoEnvios);
        _kafka.Fallar = false;

        _reloj.Avanzar(TimeSpan.FromSeconds(Opciones.CircuitoSegundosAbierto + 1));
        await _publicador.PublicarAsync(NuevoEvento(), CancellationToken.None);
        await _publicador.PublicarAsync(NuevoEvento(), CancellationToken.None);

        Assert.Equal(2, _kafka.Publicados.Count);
    }
}

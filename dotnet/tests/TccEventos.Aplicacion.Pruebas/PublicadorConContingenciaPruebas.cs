using Microsoft.Extensions.Logging.Abstractions;
using TccEventos.Aplicacion.Decoradores;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class PublicadorConContingenciaPruebas
{
    private readonly PublicadorFalso _kafka = new();
    private readonly AlmacenContingenciaEnMemoria _contingencia = new();
    private readonly PublicadorConContingencia _publicador;

    public PublicadorConContingenciaPruebas() =>
        _publicador = new(_kafka, _contingencia, NullLogger<PublicadorConContingencia>.Instance);

    private static EventoGuia NuevoEvento() =>
        new(Guid.NewGuid(), "TCC123", EstadoGuia.Recogida, DateTimeOffset.UnixEpoch, "TMS");

    [Fact]
    public async Task Con_el_broker_disponible_no_se_usa_la_contingencia()
    {
        await _publicador.PublicarAsync(NuevoEvento(), CancellationToken.None);

        Assert.Single(_kafka.Publicados);
        Assert.Empty(_contingencia.Pendientes);
    }

    [Fact]
    public async Task Si_el_broker_falla_el_evento_queda_en_contingencia_sin_error()
    {
        _kafka.Fallar = true;
        var evento = NuevoEvento();

        await _publicador.PublicarAsync(evento, CancellationToken.None);

        Assert.Equal([evento], _contingencia.Pendientes);
    }

    [Fact]
    public async Task Si_fallan_broker_y_contingencia_se_informa_que_no_es_durable()
    {
        _kafka.Fallar = true;
        _contingencia.Fallar = true;

        await Assert.ThrowsAsync<PublicacionFallidaException>(
            () => _publicador.PublicarAsync(NuevoEvento(), CancellationToken.None));
    }
}

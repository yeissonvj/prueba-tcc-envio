using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class ReenviarContingenciaPruebas
{
    private readonly PublicadorFalso _kafka = new();
    private readonly AlmacenContingenciaEnMemoria _contingencia = new();
    private readonly ReenviarContingencia _casoUso;

    public ReenviarContingenciaPruebas() => _casoUso = new(_contingencia, _kafka);

    private async Task<List<EventoGuia>> GuardarPendientes(int cantidad)
    {
        var eventos = Enumerable.Range(0, cantidad)
            .Select(i => new EventoGuia(Guid.NewGuid(), "TCC123", EstadoGuia.Recogida, DateTimeOffset.UnixEpoch.AddMinutes(i), "TMS"))
            .ToList();
        foreach (var evento in eventos)
            await _contingencia.GuardarAsync(evento, CancellationToken.None);
        return eventos;
    }

    [Fact]
    public async Task Reenvia_los_pendientes_en_orden_de_llegada_y_los_elimina()
    {
        var eventos = await GuardarPendientes(3);

        var reenviados = await _casoUso.EjecutarAsync(maximo: 10, CancellationToken.None);

        Assert.Equal(3, reenviados);
        Assert.Equal(eventos, _kafka.Publicados);
        Assert.Empty(_contingencia.Pendientes);
    }

    [Fact]
    public async Task Respeta_el_tamano_del_lote()
    {
        await GuardarPendientes(5);

        var reenviados = await _casoUso.EjecutarAsync(maximo: 2, CancellationToken.None);

        Assert.Equal(2, reenviados);
        Assert.Equal(3, _contingencia.Pendientes.Count);
    }

    [Fact]
    public async Task Si_el_broker_sigue_caido_los_pendientes_se_conservan()
    {
        var eventos = await GuardarPendientes(2);
        _kafka.Fallar = true;

        var reenviados = await _casoUso.EjecutarAsync(maximo: 10, CancellationToken.None);

        Assert.Equal(0, reenviados);
        Assert.Equal(eventos, _contingencia.Pendientes);
    }
}

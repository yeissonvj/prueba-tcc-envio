using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class NotificarCambioEstadoPruebas
{
    private readonly RegistroNotificacionesEnMemoria _registro = new();
    private readonly ProveedorEnMemoria _sms = new(CanalNotificacion.Sms);
    private readonly ProveedorEnMemoria _correo = new(CanalNotificacion.Correo);

    private NotificarCambioEstado CasoUso(Contacto? contacto = null) =>
        new(_registro, new DirectorioFijo(contacto ?? new Contacto("3001234567", "cliente@correo.co")), [_sms, _correo]);

    private static CambioEstadoGuia Cambio(EstadoGuia estado, long version = 1) =>
        new(Guid.NewGuid(), "TCC123", null, estado, DateTimeOffset.UnixEpoch, version);

    [Fact]
    public async Task Envia_por_el_canal_pedido_con_la_clave_de_idempotencia_y_lo_registra()
    {
        var resultado = await CasoUso().EjecutarAsync(Cambio(EstadoGuia.EnReparto), CanalNotificacion.Sms, CancellationToken.None);

        Assert.Equal(ResultadoNotificacion.Enviada, resultado);
        var mensaje = Assert.Single(_sms.Enviados);
        Assert.Equal(("TCC123:1:SMS", "3001234567"), (mensaje.ClaveIdempotencia, mensaje.Destino));
        Assert.Single(_registro.Enviadas);
    }

    [Fact]
    public async Task Un_estado_interno_no_se_notifica()
    {
        var resultado = await CasoUso().EjecutarAsync(Cambio(EstadoGuia.EnTransito), CanalNotificacion.Sms, CancellationToken.None);

        Assert.Equal(ResultadoNotificacion.NoAplica, resultado);
        Assert.Empty(_sms.Enviados);
    }

    [Fact]
    public async Task La_misma_notificacion_no_se_envia_dos_veces()
    {
        var cambio = Cambio(EstadoGuia.Entregada);
        await CasoUso().EjecutarAsync(cambio, CanalNotificacion.Sms, CancellationToken.None);

        var resultado = await CasoUso().EjecutarAsync(cambio, CanalNotificacion.Sms, CancellationToken.None);

        Assert.Equal(ResultadoNotificacion.YaEnviada, resultado);
        Assert.Single(_sms.Enviados);
    }

    [Fact]
    public async Task Un_reintento_viejo_no_se_envia_si_ya_se_notifico_algo_mas_reciente()
    {
        await CasoUso().EjecutarAsync(Cambio(EstadoGuia.Entregada, version: 7), CanalNotificacion.Sms, CancellationToken.None);

        var resultado = await CasoUso().EjecutarAsync(Cambio(EstadoGuia.EnReparto, version: 6), CanalNotificacion.Sms, CancellationToken.None);

        Assert.Equal(ResultadoNotificacion.Obsoleta, resultado);
        Assert.Single(_sms.Enviados);
    }

    [Fact]
    public async Task Sin_telefono_no_intenta_el_sms()
    {
        var resultado = await CasoUso(new Contacto(null, "cliente@correo.co"))
            .EjecutarAsync(Cambio(EstadoGuia.EnReparto), CanalNotificacion.Sms, CancellationToken.None);

        Assert.Equal(ResultadoNotificacion.SinDestino, resultado);
        Assert.Empty(_sms.Enviados);
    }

    [Fact]
    public async Task Si_el_proveedor_falla_no_se_registra_como_enviada()
    {
        _sms.Falla = new ProveedorNoDisponibleException("SMS caído");

        await Assert.ThrowsAsync<ProveedorNoDisponibleException>(
            () => CasoUso().EjecutarAsync(Cambio(EstadoGuia.EnReparto), CanalNotificacion.Sms, CancellationToken.None));
        Assert.Empty(_registro.Enviadas);
    }
}

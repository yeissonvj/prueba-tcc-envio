using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Consultas;
using TccEventos.Infraestructura.Postgres;

namespace TccEventos.Procesador.PruebasIntegracion.Integracion;

[Collection(ColeccionInfraestructura.Nombre)]
public class AdaptadoresPostgresPruebas(InfraestructuraReal infra) : IAsyncLifetime
{
    private static readonly DateTimeOffset Hora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));

    public Task InitializeAsync() => infra.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static EventoGuia Evento(string guia, EstadoGuia estado, DateTimeOffset cuando) =>
        new(Guid.NewGuid(), guia, estado, cuando, "TMS");

    [Fact]
    public async Task La_contingencia_es_idempotente_y_reenvia_en_orden_conservando_los_fallidos()
    {
        var almacen = new AlmacenContingenciaPostgres(infra.BaseDatos);
        var eventos = Enumerable.Range(0, 4).Select(i => Evento("TCC10", EstadoGuia.Recogida, Hora.AddMinutes(i))).ToList();
        foreach (var evento in eventos)
        {
            await almacen.GuardarAsync(evento, default);
            await Task.Delay(5); // recibido_en distinto: el orden de llegada queda definido
        }
        await almacen.GuardarAsync(eventos[0], default); // reintento del emisor durante la caída

        var publicados = new List<Guid>();
        var reenviados = await almacen.ReenviarPendientesAsync((e, _) =>
        {
            if (e.IdEvento == eventos[2].IdEvento) throw new PublicacionFallidaException("falla simulada");
            publicados.Add(e.IdEvento);
            return Task.CompletedTask;
        }, maximo: 10, default);

        Assert.Equal(3, reenviados);
        Assert.Equal([eventos[0].IdEvento, eventos[1].IdEvento, eventos[3].IdEvento], publicados);
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM contingencia_eventos WHERE id_evento = $1", eventos[2].IdEvento));
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM contingencia_eventos"));
    }

    [Fact]
    public async Task La_consulta_devuelve_el_historial_mas_reciente_primero_y_con_limite()
    {
        var procesar = new ProcesarEvento(new RepositorioGuiasPostgres(infra.BaseDatos));
        await procesar.EjecutarAsync(Evento("TCC11", EstadoGuia.Creada, Hora), default);
        for (var i = 1; i <= 120; i++)
            await procesar.EjecutarAsync(Evento("TCC11", EstadoGuia.Creada, Hora.AddMinutes(i)), default); // inválidos: solo historial

        var guia = await new ConsultaGuiasPostgres(infra.BaseDatos).ObtenerAsync("TCC11", default);

        Assert.Equal(ConsultaGuiasPostgres.MaximoHistorial, guia!.Historial.Count);
        Assert.Equal(Hora.AddMinutes(120), guia.Historial[0].OcurridoEn);
        Assert.True(guia.Historial.Zip(guia.Historial.Skip(1)).All(par => par.First.OcurridoEn >= par.Second.OcurridoEn));
        Assert.Null(await new ConsultaGuiasPostgres(infra.BaseDatos).ObtenerAsync("NOEXISTE", default));
    }

    [Fact]
    public async Task El_registro_de_notificaciones_detecta_repetidas_y_obsoletas()
    {
        var registro = new RegistroNotificacionesPostgres(infra.BaseDatos);
        CambioEstadoGuia Cambio(long version) => new(Guid.NewGuid(), "TCC12", null, EstadoGuia.EnReparto, Hora, version);

        Assert.Equal(EstadoNotificacion.Pendiente, await registro.ConsultarAsync(Cambio(6), CanalNotificacion.Sms, default));
        await registro.RegistrarEnvioAsync(Cambio(7), CanalNotificacion.Sms, default);
        await registro.RegistrarEnvioAsync(Cambio(7), CanalNotificacion.Sms, default); // idempotente

        Assert.Equal(EstadoNotificacion.YaEnviada, await registro.ConsultarAsync(Cambio(7), CanalNotificacion.Sms, default));
        Assert.Equal(EstadoNotificacion.Obsoleta, await registro.ConsultarAsync(Cambio(6), CanalNotificacion.Correo, default));
        Assert.Equal(EstadoNotificacion.Pendiente, await registro.ConsultarAsync(Cambio(7), CanalNotificacion.Correo, default));
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM notificaciones_enviadas"));
    }
}

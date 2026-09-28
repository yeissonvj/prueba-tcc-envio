using TccEventos.Notificador.Enrutamiento;

namespace TccEventos.Notificador.Pruebas;

public class OpcionesNotificadorPruebas
{
    private static readonly OpcionesNotificador TresEtapas = new()
    {
        Reintentos =
        [
            new() { Topico = "r1", Espera = TimeSpan.FromMinutes(1) },
            new() { Topico = "r2", Espera = TimeSpan.FromMinutes(10) },
            new() { Topico = "r3", Espera = TimeSpan.FromHours(1) }
        ]
    };

    [Fact]
    public void Sin_limite_se_usan_todas_las_etapas() =>
        Assert.Equal(3, TresEtapas.ConEtapasActivas().Reintentos.Count);

    [Fact]
    public void Con_limite_se_usan_solo_las_primeras()
    {
        var recortadas = new OpcionesNotificador { Reintentos = TresEtapas.Reintentos, EtapasActivas = 1, TopicoDlq = "dlq" }.ConEtapasActivas();

        Assert.Equal(["r1"], recortadas.Reintentos.Select(e => e.Topico));
        Assert.Equal("dlq", recortadas.TopicoDlq);
    }
}

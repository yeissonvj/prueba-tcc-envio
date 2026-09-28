using System.Reflection;
using TccEventos.Api.Endpoints;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Notificador.Enrutamiento;
using TccEventos.Procesador.Consumo;

namespace TccEventos.Arquitectura.Pruebas;

/// Las reglas de la arquitectura hexagonal, verificadas en cada compilación en vez de confiar en la disciplina.
/// Se leen las referencias reales de cada ensamblado compilado (equivalente a ArchUnit en Java).
public class DependenciasPruebas
{
    private static readonly Assembly Dominio = typeof(Guia).Assembly;
    private static readonly Assembly Aplicacion = typeof(ProcesarEvento).Assembly;
    private static readonly Assembly Contratos = typeof(EventoGuiaV1).Assembly;
    private static readonly Assembly Infraestructura = typeof(ProductorKafka).Assembly;
    private static readonly Assembly Api = typeof(EventosGuiaEndpoints).Assembly;
    private static readonly Assembly Procesador = typeof(ManejadorMensajeRecibido).Assembly;
    private static readonly Assembly Notificador = typeof(ManejadorNotificacion).Assembly;

    private static string[] Propias(Assembly ensamblado) => ensamblado.GetReferencedAssemblies()
        .Select(r => r.Name!)
        .Where(n => n.StartsWith("TccEventos.", StringComparison.Ordinal))
        .Order()
        .ToArray();

    private static string[] Externas(Assembly ensamblado) => ensamblado.GetReferencedAssemblies()
        .Select(r => r.Name!)
        .Where(n => !n.StartsWith("TccEventos.", StringComparison.Ordinal) && !n.StartsWith("System", StringComparison.Ordinal)
                    && n is not "netstandard" and not "mscorlib")
        .Order()
        .ToArray();

    [Fact]
    public void El_dominio_no_depende_de_ningun_otro_proyecto_ni_libreria() =>
        Assert.Equal(([], []), (Propias(Dominio), Externas(Dominio)));

    [Fact]
    public void Los_contratos_no_dependen_de_nada() =>
        Assert.Equal(([], []), (Propias(Contratos), Externas(Contratos)));

    [Fact]
    public void La_aplicacion_solo_depende_del_dominio_y_de_la_abstraccion_de_logs()
    {
        Assert.Equal(["TccEventos.Dominio"], Propias(Aplicacion));
        Assert.All(Externas(Aplicacion), n => Assert.Equal("Microsoft.Extensions.Logging.Abstractions", n));
    }

    [Fact]
    public void La_infraestructura_nunca_depende_de_un_host()
    {
        Assert.DoesNotContain(Propias(Infraestructura), n => n is "TccEventos.Api" or "TccEventos.Procesador" or "TccEventos.Notificador");
        Assert.Subset(new HashSet<string> { "TccEventos.Aplicacion", "TccEventos.Contratos", "TccEventos.Dominio" },
            Propias(Infraestructura).ToHashSet());
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Los_hosts_no_dependen_entre_si(string nombre)
    {
        var host = new[] { Api, Procesador, Notificador }.Single(h => h.GetName().Name == nombre);
        var otrosHosts = new[] { "TccEventos.Api", "TccEventos.Procesador", "TccEventos.Notificador" }.Where(h => h != nombre);

        Assert.Empty(Propias(host).Intersect(otrosHosts));
    }

    public static TheoryData<string> Hosts => new() { "TccEventos.Api", "TccEventos.Procesador", "TccEventos.Notificador" };

    [Fact]
    public void El_dominio_no_tiene_setters_publicos()
    {
        var mutables = Dominio.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance), (t, p) => (Tipo: t, Propiedad: p))
            .Where(x => x.Propiedad.SetMethod is { IsPublic: true } setter
                        && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
            .Select(x => $"{x.Tipo.Name}.{x.Propiedad.Name}");

        Assert.Empty(mutables); // los invariantes solo cambian por métodos del dominio (p. ej. Guia.Aplicar)
    }
}

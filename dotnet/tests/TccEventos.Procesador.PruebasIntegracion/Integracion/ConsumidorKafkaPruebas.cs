using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.PruebasIntegracion.Integracion;

[Collection(ColeccionInfraestructura.Nombre)]
public class ConsumidorKafkaPruebas(InfraestructuraReal infra)
{
    [Fact]
    public async Task En_paralelo_procesa_particiones_a_la_vez_sin_perder_ni_desordenar_mensajes_de_una_misma_clave()
    {
        var topico = $"paralelo.pruebas.{Guid.NewGuid():N}";
        await infra.CrearTopicoAsync(topico);
        var opciones = new OpcionesKafka { Servidores = infra.ServidoresKafka };

        // 30 claves (guías) × 10 mensajes numerados en orden de envío.
        const int claves = 30, porClave = 10;
        using (var productor = new ProductorKafka(opciones))
        {
            for (var n = 0; n < porClave; n++)
                await Task.WhenAll(Enumerable.Range(0, claves).Select(k =>
                    productor.PublicarAsync(topico, $"guia-{k}", n.ToString(), new Dictionary<string, string>(), default)));
        }

        var vistos = new ConcurrentDictionary<string, List<int>>();
        var enCurso = 0;
        var maximoEnCurso = 0;
        var total = 0;
        var terminado = new TaskCompletionSource();

        async Task Manejar(MensajeKafka mensaje, CancellationToken ct)
        {
            var actuales = Interlocked.Increment(ref enCurso);
            InterlockedMaximo(ref maximoEnCurso, actuales);
            await Task.Delay(5, ct); // simula el trabajo con la base de datos
            var lista = vistos.GetOrAdd(mensaje.Clave!, _ => []);
            lock (lista) lista.Add(int.Parse(mensaje.Valor!));
            Interlocked.Decrement(ref enCurso);
            if (Interlocked.Increment(ref total) == claves * porClave)
                terminado.TrySetResult();
        }

        var consumidor = new ConsumidorKafka(opciones,
            new SuscripcionKafka(topico, $"pruebas-{Guid.NewGuid():N}", ParticionesEnParalelo: true, CapacidadPorParticion: 8),
            Manejar, NullLoggerFactory.Instance, TimeProvider.System);

        await consumidor.StartAsync(default);
        var completo = await Task.WhenAny(terminado.Task, Task.Delay(TimeSpan.FromSeconds(60))) == terminado.Task;
        await consumidor.StopAsync(default);

        Assert.True(completo, $"Solo se procesaron {total} de {claves * porClave} mensajes");
        Assert.Equal(claves, vistos.Count);
        Assert.All(vistos, par => Assert.Equal(Enumerable.Range(0, porClave), par.Value)); // orden por clave intacto
        Assert.True(maximoEnCurso > 1, "Las particiones no se procesaron en paralelo");
    }

    private static void InterlockedMaximo(ref int destino, int valor)
    {
        int actual;
        while (valor > (actual = Volatile.Read(ref destino)) && Interlocked.CompareExchange(ref destino, valor, actual) != actual) { }
    }
}

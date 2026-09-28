using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Procesador.Consumo;

namespace TccEventos.Procesador.PruebasIntegracion.Falsos;

public class DlqEnMemoria : IDestinoDlq
{
    public List<MensajeRechazado> Rechazados { get; } = [];
    public int FallasPendientes { get; set; }

    public Task EnviarAsync(MensajeRechazado rechazado, CancellationToken ct)
    {
        if (FallasPendientes > 0)
        {
            FallasPendientes--;
            throw new PublicacionFallidaException("DLQ no disponible (simulado)");
        }

        Rechazados.Add(rechazado);
        return Task.CompletedTask;
    }
}

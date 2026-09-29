using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Procesador.Configuracion;

// ============================================================================================
// Punto de entrada del procesador de estado (worker, sin HTTP).
// Consume guias.eventos.recibidos, aplica cada evento a su guía (inbox + estado + outbox en una
// transacción) y publica los cambios confirmados en guias.estados.cambiados.
// ============================================================================================

var builder = Host.CreateApplicationBuilder(args);

// Servicios del procesador (consumidor, caso de uso, repositorio, DLQ y relay) y observabilidad.
builder.Services.AgregarProcesador(builder.Configuration);
builder.AgregarObservabilidad("tcc-procesador");

var host = builder.Build();
host.Run();

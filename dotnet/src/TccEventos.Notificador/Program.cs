using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Notificador.Configuracion;

// ============================================================================================
// Punto de entrada del notificador (worker, sin HTTP).
// Consume guias.estados.cambiados y los tópicos de reintento, y avisa al cliente por SMS o correo
// sin bloquear nunca las notificaciones de las demás guías.
// ============================================================================================

var builder = Host.CreateApplicationBuilder(args);

// Servicios del notificador (proveedores con circuito, consumidores y enrutador) y observabilidad.
builder.Services.AgregarNotificador(builder.Configuration);
builder.AgregarObservabilidad("tcc-notificador");

var host = builder.Build();
host.Run();

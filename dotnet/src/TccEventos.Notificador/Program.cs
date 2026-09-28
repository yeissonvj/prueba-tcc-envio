using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Notificador.Configuracion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AgregarNotificador(builder.Configuration);
builder.AgregarObservabilidad("tcc-notificador");

var host = builder.Build();
host.Run();

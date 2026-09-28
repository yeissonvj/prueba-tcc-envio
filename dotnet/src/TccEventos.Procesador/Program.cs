using TccEventos.Infraestructura.Observabilidad;
using TccEventos.Procesador.Configuracion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AgregarProcesador(builder.Configuration);
builder.AgregarObservabilidad("tcc-procesador");

var host = builder.Build();
host.Run();

using TccEventos.Api.Configuracion;
using TccEventos.Api.Endpoints;
using TccEventos.Api.Errores;
using TccEventos.Api.Salud;
using TccEventos.Api.Seguridad;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TccEventos.Infraestructura.Observabilidad;

var builder = WebApplication.CreateBuilder(args);

// Un evento pesa ~1 KB; 64 KB evita que un cuerpo gigante agote la memoria (defecto: 30 MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);

builder.Services.AgregarDocumentacion();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorPeticionInvalida>();
builder.Services.AddExceptionHandler<ManejadorPublicacionFallida>();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AgregarIngesta(builder.Configuration);
builder.Services.AgregarSalud();
builder.Services.AgregarSeguridad(builder.Configuration);
builder.AgregarObservabilidad("tcc-api",
    trazas: t => t.AddAspNetCoreInstrumentation(o => o.Filter = http => !http.Request.Path.StartsWithSegments("/salud")),
    metricas: m => m.AddAspNetCoreInstrumentation());

var app = builder.Build();

app.UseExceptionHandler();
app.UsarSeguridad();

app.MapDocumentacion();
app.MapSalud();
app.MapEventosGuia();
app.MapGuias();

await app.CalentarDependenciasAsync();
app.Run();

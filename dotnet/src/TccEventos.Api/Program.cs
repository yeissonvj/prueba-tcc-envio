using TccEventos.Api.Configuracion;
using TccEventos.Api.Endpoints;
using TccEventos.Api.Errores;
using TccEventos.Api.Salud;
using TccEventos.Api.Seguridad;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TccEventos.Infraestructura.Observabilidad;

// ============================================================================================
// Punto de entrada de la API de ingesta y consulta (host HTTP).
//   POST /api/v1/eventos-guia   recibe eventos y los deja durables (Kafka o contingencia).
//   GET  /api/v1/guias/{numero} consulta estado e historial.
//   GET  /salud/viva, /salud/lista sondas para el orquestador.
// Aquí solo se arma la aplicación; qué adaptador implementa cada puerto se decide en RegistroServicios.
// ============================================================================================

var builder = WebApplication.CreateBuilder(args);

// Un evento pesa ~1 KB; 64 KB evita que un cuerpo gigante agote la memoria (defecto: 30 MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);

// Servicios: documentación, errores como ProblemDetails, casos de uso y adaptadores, salud, seguridad y observabilidad.
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

// Tubería HTTP: primero el manejo de errores, luego autenticación, autorización y límite por cliente.
app.UseExceptionHandler();
app.UsarSeguridad();

// Rutas.
app.MapDocumentacion();
app.MapSalud();
app.MapEventosGuia();
app.MapGuias();

// Precalienta Kafka, Redis y las llaves del emisor de tokens para que la primera petición no pague ese costo.
await app.CalentarDependenciasAsync();
app.Run();

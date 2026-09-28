using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TccEventos.Infraestructura.Observabilidad;

public static class RegistroObservabilidad
{
    /// Trazas, métricas y logs por OTLP hacia el Collector (Observabilidad:OtlpEndpoint; vacío = no exporta,
    /// útil en pruebas). Además, logs en la consola: JSON con TraceId/SpanId fuera de Desarrollo.
    public static IHostApplicationBuilder AgregarObservabilidad(
        this IHostApplicationBuilder builder,
        string nombreServicio,
        Action<TracerProviderBuilder>? trazas = null,
        Action<MeterProviderBuilder>? metricas = null)
    {
        var endpoint = builder.Configuration["Observabilidad:OtlpEndpoint"];
        var exportar = !string.IsNullOrWhiteSpace(endpoint);

        // Primero la consola: ClearProviders quita TODOS los proveedores de logs, así que debe ir antes
        // de registrar el de OpenTelemetry (si fuera después, los logs dejarían de exportarse en silencio).
        builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        if (!builder.Environment.IsDevelopment())
        {
            // Solo JSON (sin el formato de texto por defecto, que duplicaría cada línea).
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(
                nombreServicio,
                serviceVersion: typeof(RegistroObservabilidad).Assembly.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName))
            .WithTracing(t =>
            {
                t.AddSource(Telemetria.Nombre).AddNpgsql();
                trazas?.Invoke(t);
                if (exportar) t.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint!));
            })
            .WithMetrics(m =>
            {
                m.AddMeter(Telemetria.Nombre).AddRuntimeInstrumentation();
                metricas?.Invoke(m);
                // Cada 10 s (por defecto son 60 s: demasiado lento para ver un pico en el tablero).
                if (exportar) m.AddOtlpExporter((o, lector) =>
                {
                    o.Endpoint = new Uri(endpoint!);
                    lector.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                });
            })
            // Logs hacia Loki (vía Collector). Cada registro lleva trace_id y span_id: desde un log en Grafana
            // se salta a su traza en Jaeger.
            .WithLogging(
                l => { if (exportar) l.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint!)); },
                o =>
                {
                    o.IncludeFormattedMessage = true;
                    o.IncludeScopes = true;
                });

        return builder;
    }
}

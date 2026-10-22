using System.Net;
using System.Reflection;
using Arc4u.Authorization;
using Arc4u.Diagnostics;
using Dev4u.CampusHub.Rights;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Dev4u.CampusHub.ServiceDefaults;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1034:Nested types should not be visible",
    Justification = "C# 14 extension block; nesting is required by the language feature.")]
public static class HostingExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddServiceDefaults()
        {
            builder.ConfigureOpenTelemetry();

            builder.AddDefaultHealthChecks();

            builder.Services.AddServiceDiscovery();

            // Teachers and administrators (see CampusHubRoles), read by the claims filler of every service.
            builder.Services.Configure<RoleOptions>(builder.Configuration.GetSection(RoleOptions.SectionName));

            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                // Turn on resilience by default
                http.AddStandardResilienceHandler();

                // Turn on service discovery by default
                http.AddServiceDiscovery();

                // HTTP/2 for every factory-created client
                http.ConfigureHttpClient(client =>
                {
                    client.DefaultRequestVersion = HttpVersion.Version20;      // System.Net.HttpVersion
                    client.DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher;
                });                
            });

            return builder;
        }

        private void ConfigureOpenTelemetry()
        {
            var configuration = builder.Configuration;
            var openTelemetrySettings = new OpenTelemetrySettings();
            configuration.GetSection("OpenTelemetry").Bind(openTelemetrySettings);

            // Used by HangfireDashboardSpanFilterProcessor to recognise the request a span belongs to.
            builder.Services.AddHttpContextAccessor();

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    // Start from the default resource builder so the OTEL_SERVICE_NAME and
                    // OTEL_RESOURCE_ATTRIBUTES variables published by Aspire (or a collector) are honoured
                    // and every signal - logs, traces and metrics - is reported under the same resource.
                    resource.AddAttributes(new List<KeyValuePair<string, object>>
                        {
                            new("host.name", System.Environment.MachineName),
                            new("service.version",
                                Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "1.0.0"),
                            new("deployment.environment",
                                configuration["Application.configuration:Environment:Name"] ?? "Production"),
                        })
                        .AddAttributes(openTelemetrySettings.Attributes.ToList());
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddAspNetCoreInstrumentation();
                    metrics.AddHttpClientInstrumentation();
                    metrics.AddRuntimeInstrumentation();
                    // Required for ASP.NET Core metrics
                    metrics.AddMeter("Microsoft.AspNetCore.Hosting");
                    metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
                    metrics.AddMeter("Microsoft.AspNetCore.Routing");
                    metrics.AddMeter("Microsoft.AspNetCore.Diagnostics");
                    metrics.AddMeter("Microsoft.AspNetCore.Http"); // HTTP server semantic convention metrics
                })
                .WithTracing(tracing =>
                {
                    tracing.AddSource(openTelemetrySettings.Sources.ToArray())
                    .AddAspNetCoreInstrumentation((options) => options.Filter = httpContext =>
                    {
                        var path = httpContext.Request.Path.Value;

                        if (string.IsNullOrEmpty(path))
                        {
                            return false;
                        }

                        // Exclude the Hangfire dashboard, including the spans started while handling it.
                        if (HangfireDashboardSpanFilterProcessor.IsHangfireDashboardRead(httpContext.Request.Method, path))
                        {
                            HangfireDashboardSpanFilterProcessor.MarkRequest(httpContext);
                            return false;
                        }

                        // Exclude health check endpoints from telemetry
                        return !SharedRegExs.RegexHealthCheckEndpoints().IsMatch(path)
                               && !SharedRegExs.RegexCoreInstrumentationFilter().IsMatch(path);
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.FilterHttpRequestMessage = (httpRequestMessage) =>
                        {
                            // Return false to exclude from tracing
                            if (httpRequestMessage.RequestUri == null)
                            {
                                return false;
                            }

                            // AbsolutePath ignores the scheme, so http and https calls are both filtered.
                            var path = httpRequestMessage.RequestUri.AbsolutePath;

                            return !SharedRegExs.RegexHealthCheckEndpoints().IsMatch(path)
                                   && !HangfireDashboardSpanFilterProcessor.IsHangfireDashboardRead(httpRequestMessage.Method.Method, path);
                        };
                    })
                    .AddGrpcClientInstrumentation(options => { options.SuppressDownstreamInstrumentation = true; })
                    // Registered before AddOtlpExporter() so the export processor it adds sees the
                    // cleared Recorded flag and skips our own telemetry-shipping spans.
                    .AddProcessor(new OtlpSelfExportFilterProcessor())
                    .AddProcessor(sp => new HangfireDashboardSpanFilterProcessor(sp.GetRequiredService<IHttpContextAccessor>()));
                });

            builder.AddOpenTelemetryExporters();
        }

        private void AddOpenTelemetryExporters()
        {
            if (builder is WebApplicationBuilder webBuilder)
            {
                webBuilder.Host.UseSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration));
            }

            var useOtlpExporter = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

            if (useOtlpExporter)
            {
                Log.Information("Using OTLP exporter.");

                builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
                {
                    tracing.AddOtlpExporter();
                });

                builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
                {
                    metrics.AddOtlpExporter();
                });
            }
            else
            {
                Log.Warning("No OTEL_EXPORTER_OTLP_ENDPOINT is defined: logs, traces and metrics are not exported.");
            }
        }

        private void AddDefaultHealthChecks()
        {
            builder.Services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        }
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // No welcome page here: each service registers its own, and "/" of the gateway serves the Blazor front.
        app.MapHealthChecks("/health/ready",
            new HealthCheckOptions() { Predicate = (check) => check.Tags.Contains("ready"), });
        app.MapHealthChecks("/health/live",
            new HealthCheckOptions() { Predicate = (check) => check.Tags.Contains("live") });
        app.MapHealthChecks("healthz",
            new HealthCheckOptions()
            {
                Predicate = _ => true, ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
        return app;
    }

    public static PoliciesBuilder AddDefaultAuthorizationPolicies(this IServiceCollection services)
    {
        return services.AddScopedOperationsPolicy(Operations.Scopes, Operations.Values);

        // Add policies you need for all services.
    }
}

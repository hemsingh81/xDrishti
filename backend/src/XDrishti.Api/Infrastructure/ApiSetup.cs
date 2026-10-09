using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using XDrishti.Api.Endpoints;

namespace XDrishti.Api.Infrastructure;

internal static class ApiSetup
{
    public const string OpenApiRoute = "/api/openapi/{documentName}.json";

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "xDrishti API";
            document.Info.Version = "v1";
            return Task.CompletedTask;
        }));
        services.ConfigureHttpJsonOptions(o =>
        {
            // Numbers are numbers on the wire (no "123" strings): keeps the contract and generated types exact.
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        // Endpoint modules are registered explicitly — one line per feature module.
        services.AddSingleton<IEndpointModule, SystemEndpoints>();
        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging(o => o.GetLevel = RequestLogLevel.ForPath);

        // Liveness: process is up. Readiness: dependencies (database) reachable.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

        app.MapOpenApi(OpenApiRoute);
        // Local-only docs UI: Scalar's cloud features (AI agent, MCP, telemetry, client button) are disabled.
        app.MapScalarApiReference("/api/docs", o => o
            .WithTitle("xDrishti API")
            .WithOpenApiRoutePattern(OpenApiRoute)
            .DisableAgent()
            .DisableMcp()
            .DisableTelemetry()
            .HideClientButton());

        var api = app.MapGroup("/api");
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.Map(api);
        }

        return app;
    }
}

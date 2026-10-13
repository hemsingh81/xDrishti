using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Serilog;
using XDrishti.Api.Auth;
using XDrishti.Api.Endpoints;
using XDrishti.Api.Market;
using XDrishti.Api.Platform;
using XDrishti.Api.Portfolio;

namespace XDrishti.Api.Infrastructure;

internal static class ApiSetup
{
    public const string OpenApiRoute = "/api/openapi/{documentName}.json";

    /// <summary>Failed and successful logins together: more than this per minute from one address are refused with 429.</summary>
    public const int LoginAttemptsPerMinute = 10;

    private static readonly string[] TrustedProxyNetworks = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "xDrishti API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            o.AddSchemaTransformer(ProblemDetailsSchema.AddExtensions);
        });
        services.ConfigureHttpJsonOptions(o =>
        {
            // Numbers are numbers on the wire (no "123" strings): keeps the contract and generated types exact.
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        services.AddAuth();

        // Endpoint modules are registered explicitly — one line per feature module.
        services.AddSingleton<IEndpointModule, SystemEndpoints>();
        services.AddSingleton<IEndpointModule, AuthEndpoints>();
        services.AddSingleton<IEndpointModule, AccountsEndpoints>();
        services.AddSingleton<IEndpointModule, BrokersEndpoints>();
        services.AddSingleton<IEndpointModule, RiskProfilesEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioBucketsEndpoints>();
        services.AddSingleton<IEndpointModule, InstrumentsEndpoints>();
        services.AddSingleton<IEndpointModule, BasketsEndpoints>();
        services.AddSingleton<IEndpointModule, HoldingsEndpoints>();
        services.AddSingleton<IEndpointModule, PortfolioHistoryEndpoints>();
        services.AddSingleton<IEndpointModule, DataEndpoints>();
        services.AddSingleton<IEndpointModule, DataQualityEndpoints>();
        services.AddSingleton<IEndpointModule, DataCsvEndpoints>();
        services.AddSingleton<IEndpointModule, CorporateActionEndpoints>();
        services.AddSingleton<IEndpointModule, ServicesEndpoints>();

        // Instrument master (ADR 0010): the worker that carries out registered refreshes.
        services.AddHostedService<RefreshJobWorker>();

        // Market data (ADR 0016): the worker that carries out registered data jobs (CSV load, Dhan fetch, aggregates).
        services.AddHostedService<DataJobWorker>();

        // The services framework (ADR 0018): the one scheduler of the automatic jobs (end-of-day data run, instrument master refresh); their times are edited on the Services screen.
        services.AddHostedService<ServiceSchedulerHost>();

        // Holdings (ADR 0012): the automatic refresh of the accounts that switched it on.
        services.AddHostedService<PortfolioRefreshService>();
        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging(o =>
        {
            o.GetLevel = RequestLogLevel.ForPath;
            o.Logger = app.Services.GetRequiredService<Serilog.ILogger>(); // the host's logger (HostDefaults keeps the static one out)
        });

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<StepUpMiddleware>();

        // Liveness: process is up. Readiness: dependencies (database) reachable. Public: container probes use them.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        // The contract and the docs UI need a session like everything else (stated explicitly; the fallback policy would also cover them).
        app.MapOpenApi(OpenApiRoute).RequireAuthorization();
        // Local-only docs UI: Scalar's cloud features (AI agent, MCP, telemetry, client button) are disabled.
        app.MapScalarApiReference("/api/docs", o => o
            .WithTitle("xDrishti API")
            .WithOpenApiRoutePattern(OpenApiRoute)
            .DisableAgent()
            .DisableMcp()
            .DisableTelemetry()
            .HideClientButton())
            .RequireAuthorization();

        var api = app.MapGroup("/api");
        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.Map(api);
        }

        return app;
    }

    private static void AddAuth(this IServiceCollection services)
    {
        services.AddAuthentication(SessionAuthentication.Scheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthentication.Scheme, _ => { });

        // Closed by default: any endpoint without its own policy needs a signed-in session.
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // The proxy (Caddy, container network) is trusted to tell the real client address and scheme.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var network in TrustedProxyNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(SessionAuthentication.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (context, _) =>
            {
                int? retryAfter = null;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window))
                {
                    retryAfter = (int)Math.Ceiling(window.TotalSeconds);
                    context.HttpContext.Response.Headers.RetryAfter = retryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await ProblemResults.WriteAsync(context.HttpContext, ProblemResults.Create(
                    StatusCodes.Status429TooManyRequests, "Too many attempts. Try again later.", SessionAuthentication.RateLimitedCode, retryAfter));
            };
        });
    }
}

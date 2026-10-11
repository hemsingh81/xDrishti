using XDrishti.Api.Auth;
using XDrishti.Api.Infrastructure;
using XDrishti.Application;
using XDrishti.Hosting;
using XDrishti.Infrastructure;
using XDrishti.Infrastructure.Persistence;

// Container health probe: `dotnet XDrishti.Api.dll --healthcheck` (chiseled images have no curl).
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddXDrishtiDefaults("xd-api");

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi();

// The API serves the broker vault: it refuses to start without its key. Only the build-time contract generation runs without secrets.
if (!OpenApiGeneration.IsRunning)
{
    builder.Services.RequireBrokerVaultKey();
}

var app = builder.Build();

// One-shot deployment step: `dotnet XDrishti.Api.dll migrate` (also provisions the owner account when none exists).
if (args.Contains("migrate", StringComparer.Ordinal))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    return await OwnerBootstrap.ProvisionAsync(app.Services);
}

// One-shot: `dotnet XDrishti.Api.dll owner-reset` replaces the owner's password from the secrets and ends all sessions.
if (args.Contains("owner-reset", StringComparer.Ordinal))
{
    return await OwnerBootstrap.ResetAsync(app.Services);
}

app.UseApi();
await app.RunAsync();
return 0;

/// <summary>Entry point type, public for WebApplicationFactory in integration tests.</summary>
public partial class Program;

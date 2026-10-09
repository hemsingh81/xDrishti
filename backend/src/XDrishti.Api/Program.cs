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

var app = builder.Build();

// One-shot deployment step: `dotnet XDrishti.Api.dll migrate`.
if (args.Contains("migrate", StringComparer.Ordinal))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    return 0;
}

app.UseApi();
await app.RunAsync();
return 0;

/// <summary>Entry point type, public for WebApplicationFactory in integration tests.</summary>
public partial class Program;

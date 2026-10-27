using XDrishti.Api.Auth;
using XDrishti.Api.Cli;
using XDrishti.Api.Context;
using XDrishti.Api.CostSheets;
using XDrishti.Api.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application;
using XDrishti.Hosting;
using XDrishti.Infrastructure;
using XDrishti.Infrastructure.Persistence;

// The verb is chosen by the first argument alone and tested first, so that a symbol list (`simulate --symbols migrate`) never selects another step (spec P3-04b2, D2, ADR 0041).
var verb = VerbDispatch.Of(args);
if (verb == Verb.Simulate)
{
    return await SimulateProgram.RunProcessAsync(args[1..]);
}

// The cost-sheet verb (spec P3-03f) is chosen the same way: `cost-sheet load migrate` or a file named owner-reset never selects another step.
if (verb == Verb.CostSheet)
{
    return await CostSheetProgram.RunProcessAsync(args[1..]);
}

// The context verb (P3-01d) is chosen the same way: `context refresh` never selects another step.
if (verb == Verb.Context)
{
    return await ContextProgram.RunProcessAsync(args[1..]);
}

// The verify-pack verb (spec P3-09) is chosen the same way, by the first argument alone.
if (verb == Verb.VerifyPack)
{
    return await SimulateProgram.RunProcessAsync(args[1..], verifyPack: true);
}

// The xd verb (spec P5-07, ADR 0061) is chosen the same way.
if (verb == Verb.Xd)
{
    return await XdProgram.RunProcessAsync(args[1..]);
}

// Container health probe: `dotnet XDrishti.Api.dll --healthcheck` (chiseled images have no curl).
if (verb == Verb.HealthCheck)
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
if (verb == Verb.Migrate)
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    await StrategySeeding.SeedAsync(app.Services); // the built-in strategies as stored text (spec P3-02d); idempotent
    await PostMigration.ProvisionReadOnlyRolesAsync(app.Services); // the assistant's read-only roles get a login when both password files exist (spec P9-02); idempotent, otherwise they stay NOLOGIN
    return await OwnerBootstrap.ProvisionAsync(app.Services);
}

// One-shot: `dotnet XDrishti.Api.dll owner-reset` replaces the owner's password from the secrets and ends all sessions.
if (verb == Verb.OwnerReset)
{
    return await OwnerBootstrap.ResetAsync(app.Services);
}

app.UseApi();
await app.RunAsync();
return 0;

/// <summary>Entry point type, public for WebApplicationFactory in integration tests.</summary>
public partial class Program;

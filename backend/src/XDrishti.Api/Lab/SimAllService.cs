using XDrishti.Application.SimAll;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the loop of the simulate-everything job in <c>xd-api</c> (spec AU-12, ADR 0248): all logic is in <see cref="SimAllRunner"/>. With no job it only looks at the table. Off when the configuration says <c>Simulate:RunWorker=false</c> (the integration tests drive the runner themselves).</summary>
internal sealed class SimAllService(SimAllRunner runner, SimAllSettings settings) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        settings.RunWorker ? runner.RunAsync(stoppingToken) : Task.CompletedTask;
}

using XDrishti.Application.Replay.Sim;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the loop of the day-by-day simulation in <c>xd-api</c> (SR-04, ADR 0293): all logic is in <see cref="SimDaysRunner"/>. Off when the configuration says <c>Sim:RunWorker=false</c> (the integration tests drive the runner themselves).</summary>
internal sealed class SimDaysService(SimDaysRunner runner, SimDaysOptions options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.RunWorker ? runner.RunAsync(stoppingToken) : Task.CompletedTask;
}

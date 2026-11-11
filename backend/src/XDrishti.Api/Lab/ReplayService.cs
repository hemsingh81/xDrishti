using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the loop of the system replay job in <c>xd-api</c> (spec SR-01 part B, ADR 0272): all logic is in <see cref="ReplayRunner"/>. With no queued run it only looks at the table. Off when the configuration says <c>Replay:RunWorker=false</c> (the integration tests drive the runner themselves).</summary>
internal sealed class ReplayService(ReplayRunner runner, SystemReplayOptions options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.RunWorker ? runner.RunAsync(stoppingToken) : Task.CompletedTask;
}

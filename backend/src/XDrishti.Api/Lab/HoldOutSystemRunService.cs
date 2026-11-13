using XDrishti.Application.Replay.HoldOut;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the loop that runs the claimed system replays over a hold-out block in <c>xd-api</c> (spec SR-03): all logic is in <see cref="HoldOutSystemRunLoop"/>. Off when <c>Replay:RunWorker=false</c> (the tests drive the runner).</summary>
internal sealed class HoldOutSystemRunService(HoldOutSystemRunLoop loop) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => loop.RunAsync(stoppingToken);
}

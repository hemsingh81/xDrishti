using XDrishti.Application.Abstractions.Ticks;

namespace XDrishti.Feed;

/// <summary>The third background loop of the host: hands the whole life of the trade-tick recorder to its supervisor and stops it with the host. Its own loop and its own try/catch: it can neither stall nor fail the live feed or the depth recorder.</summary>
internal sealed class TickService(ITickSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}

using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Feed;

/// <summary>The second background loop of the host: hands the whole life of the depth recorder - gates, window, run record, connection, disk guard, heartbeat - to its supervisor and stops it with the host. Its own loop and its own try/catch: it can neither stall nor fail the live feed.</summary>
internal sealed class DepthService(IDepthSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}

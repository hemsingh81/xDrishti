using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Feed;

/// <summary>The one background loop of the host: hands the whole life of the feed - window, run record, connection, heartbeat - to the supervisor and stops it with the host.</summary>
internal sealed class FeedService(IFeedSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}

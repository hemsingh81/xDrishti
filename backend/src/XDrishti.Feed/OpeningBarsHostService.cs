using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Feed;

/// <summary>The fifth background loop of the host: hands the whole life of the opening-minute recorder to its supervisor and stops it with the host. Its own loop and its own try/catch: it can neither stall nor fail the live feed, the depth recorder or the tick recorder.</summary>
internal sealed class OpeningBarsHostService(IOpeningBarsSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}

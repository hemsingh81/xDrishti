using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Feed;

/// <summary>The fifth background loop of the host: hands the whole life of the expired-options backfill to its supervisor and stops it with the host. Its own loop and its own try/catch: it can neither stall nor fail the live feed or the other recorders.</summary>
internal sealed class OptionBackfillService(IOptionBackfillSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}

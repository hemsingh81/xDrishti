using XDrishti.Application.Market.Refresh;

namespace XDrishti.Api.Market;

/// <summary>Hosts the daily automatic refresh of the instrument master; the schedule and the work are in <see cref="DailyRefreshRunner"/>.</summary>
internal sealed class DailyRefreshService(DailyRefreshRunner runner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => runner.RunAsync(stoppingToken);
}

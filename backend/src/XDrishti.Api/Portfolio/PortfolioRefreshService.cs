using XDrishti.Application.Portfolio.Refresh;

namespace XDrishti.Api.Portfolio;

/// <summary>Hosts the automatic holdings refresh; the schedule and the work are in <see cref="PortfolioRefreshRunner"/>.</summary>
internal sealed class PortfolioRefreshService(PortfolioRefreshRunner runner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => runner.RunAsync(stoppingToken);
}

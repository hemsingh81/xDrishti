using XDrishti.Application.Market.Bars.Eod;

namespace XDrishti.Api.Market;

/// <summary>Hosts the scheduled end-of-day data run (ADR 0017 §2); the schedule, the catch-up and the work are in <see cref="DataEodRunner"/>.</summary>
internal sealed class DataEodService(DataEodRunner runner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => runner.RunAsync(stoppingToken);
}

using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Eod;

namespace XDrishti.Infrastructure.Market.Eod;

internal sealed class EodSettings(IOptions<HistoryOptions> options) : IEodSettings
{
    public bool Enabled => options.Value.Nse.Enabled;

    public IReadOnlyList<string> Series => [.. options.Value.Nse.Series.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0)];

    public int MaxPerDay => options.Value.Nse.MaxPerDay;

    public TimeSpan Delay => TimeSpan.FromMilliseconds(options.Value.Nse.DelayMs);

    public int Years => options.Value.Nse.Years;

    public double VolumeTolerancePct => options.Value.Quality.DailyVolumeTolerancePct;
}

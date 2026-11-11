using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The stored daily bars of one call, each instrument read once however many baskets share it (BK-07 criterion 1), from the window start less the history the 200-day average, the 252-session high and the 60-day correlation need. The count of reads is logged.
/// </summary>
internal sealed class ProfileBars(IBarQueries bars)
{
    /// <summary>Calendar days read before the window start: 252 sessions and some slack.</summary>
    public const int LookbackDays = 420;

    private readonly Dictionary<Guid, (IReadOnlyList<DailyClose> Closes, IReadOnlyList<DailyVolume> Volumes)> _cache = [];

    /// <summary>Daily-bar queries made so far.</summary>
    public int Reads { get; private set; }

    public async ValueTask<(IReadOnlyList<DailyClose> Closes, IReadOnlyList<DailyVolume> Volumes)> ReadAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(instrumentId, out var cached))
        {
            return cached;
        }

        Reads++;
        var rows = await bars.ReadDailyBarsAsync(instrumentId, from.AddDays(-LookbackDays), to.AddDays(1), cancellationToken);
        var read = (
            (IReadOnlyList<DailyClose>)[.. rows.Select(r => new DailyClose(IndiaTime.Today(r.Ts), r.Close))],
            (IReadOnlyList<DailyVolume>)[.. rows.Select(r => new DailyVolume(IndiaTime.Today(r.Ts), r.Volume))]);
        _cache[instrumentId] = read;
        return read;
    }
}

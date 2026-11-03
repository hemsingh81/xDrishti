using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The bars of one rolling series (all offsets) for a bounded stretch, indexed by bar time: the grid of times any offset has, and the bars at a time. Built once per series and stretch and shared by every trade on it; read-only afterwards. Pure.
/// </summary>
public sealed class BarGrid
{
    private readonly DateTimeOffset[] _times;
    private readonly Dictionary<DateTimeOffset, List<OptionBar>> _byTs;

    private BarGrid(DateTimeOffset[] times, Dictionary<DateTimeOffset, List<OptionBar>> byTs)
    {
        _times = times;
        _byTs = byTs;
    }

    /// <summary>Every bar time of the series, oldest first.</summary>
    public IReadOnlyList<DateTimeOffset> Times => _times;

    public static BarGrid Build(IEnumerable<OptionBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var byTs = new Dictionary<DateTimeOffset, List<OptionBar>>();
        foreach (var bar in bars)
        {
            var at = bar.Ts.ToUniversalTime();
            if (!byTs.TryGetValue(at, out var list))
            {
                byTs[at] = list = [];
            }

            list.Add(bar);
        }

        return new BarGrid(byTs.Keys.Order().ToArray(), byTs);
    }

    /// <summary>The index of the first bar time at or after <paramref name="at"/>; -1 when there is none.</summary>
    public int FirstAtOrAfter(DateTimeOffset at)
    {
        var index = Array.BinarySearch(_times, at.ToUniversalTime());
        index = index < 0 ? ~index : index;
        return index < _times.Length ? index : -1;
    }

    /// <summary>The index of the last bar time on the India date; -1 when the date has none.</summary>
    public int LastOnDate(DateOnly date)
    {
        var last = -1;
        var first = FirstAtOrAfter(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset));
        for (var i = first; i >= 0 && i < _times.Length && SessionClock.DateOf(_times[i].ToUnixTimeSeconds()) == date; i++)
        {
            last = i;
        }

        return last;
    }

    /// <summary>The bars at the time (any offset); empty when the time is not on the grid.</summary>
    public IReadOnlyList<OptionBar> At(DateTimeOffset at) => _byTs.TryGetValue(at.ToUniversalTime(), out var list) ? list : [];

    /// <summary>The bar of the offset at the time, whatever its flags; null when that offset has none.</summary>
    public OptionBar? Row(DateTimeOffset at, int offset) => At(at).FirstOrDefault(b => b.Offset == offset);
}

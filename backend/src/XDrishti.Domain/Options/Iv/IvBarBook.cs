using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// The stored bars of the series an extraction chunk reads, indexed for "the last bar that has ended at or before the decision instant" (P14-11, ADR 0199, pure). A bar is stamped with its start (the stitching check's reading); it has ended at <c>Ts + interval</c>. Only bars of the instant's own India date count:
/// an earlier session's last bar is a different day's IV and is never carried forward. A bar that ends after the instant is never returned, whatever the book holds.
/// </summary>
public sealed class IvBarBook
{
    private readonly Dictionary<(int Code, string Side), Side> _sides = [];

    /// <summary>Adds the bars of one side of one series; bars of one timestamp (all offsets) form a slice.</summary>
    public void Add(int code, string side, int intervalMinutes, IEnumerable<OptionBar> bars)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(bars);
        var slices = new List<IvSlice>();
        foreach (var group in bars.GroupBy(b => b.Ts).OrderBy(g => g.Key))
        {
            slices.Add(new IvSlice(group.Key, group.GroupBy(b => b.Offset).ToDictionary(g => g.Key, g => g.Last())));
        }

        _sides[(code, side)] = new Side(TimeSpan.FromMinutes(intervalMinutes), slices);
    }

    /// <summary>The slice with the latest start whose bar has ended at or before <paramref name="t"/> on the India date of <paramref name="t"/>; null when there is none.</summary>
    public IvSlice? Last(int code, string side, DateTimeOffset t)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (!_sides.TryGetValue((code, side), out var book))
        {
            return null;
        }

        var low = 0;
        var high = book.Slices.Count - 1;
        IvSlice? found = null;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (book.Slices[mid].Ts + book.Interval <= t)
            {
                found = book.Slices[mid];
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found is not null && IndiaTime.Today(found.Ts) == IndiaTime.Today(t) ? found : null;
    }

    /// <summary>
    /// The CE and PE slices of one code at the same start: the latest start that both sides have, ended at or before <paramref name="t"/> on its India date. When only one side has any slice, that one and null; when both have slices but never at one start, the CE slice and null (a pair that was
    /// not taken together is not a pair). Both null when neither has one.
    /// </summary>
    public (IvSlice? Ce, IvSlice? Pe) LastPair(int code, DateTimeOffset t)
    {
        var ce = Last(code, "CE", t);
        var pe = Last(code, "PE", t);
        if (ce is null || pe is null)
        {
            return (ce, pe);
        }

        var limit = t;
        var interval = _sides[(code, "CE")].Interval;
        while (ce is not null && pe is not null && ce.Ts != pe.Ts)
        {
            limit = (ce.Ts > pe.Ts ? ce.Ts : pe.Ts) + interval - TimeSpan.FromTicks(1);
            ce = Last(code, "CE", limit);
            pe = Last(code, "PE", limit);
        }

        return ce is not null && pe is not null ? (ce, pe) : (Last(code, "CE", t), null);
    }

    private sealed record Side(TimeSpan Interval, List<IvSlice> Slices);
}

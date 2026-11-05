using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Shadow;

/// <summary>One counted paper trade of the behavioural series with the candidates its entry day had and its exit day.</summary>
/// <param name="Trade">The counted trade.</param>
/// <param name="DayCandidates">Counted trades entered on the same India day (the design effect is built from it).</param>
/// <param name="ExitDay">The India day of the exit.</param>
internal sealed record BehaviouralFigure(CountedTrade Trade, int DayCandidates, DateOnly ExitDay);

/// <summary>The one place that selects the P10 behavioural series (the counted, finite trades of the epoch, optionally of one strategy, in exit order), shared by the sequential test and the decay report so that they read one series.</summary>
internal static class BehaviouralSeries
{
    public static IReadOnlyList<BehaviouralFigure> Select(PaperCompareData data, string? prefix)
    {
        ArgumentNullException.ThrowIfNull(data);
        IReadOnlyList<PaperCompareRow> rows = prefix is null ? data.Rows : [.. data.Rows.Where(r => r.TradeKey.StartsWith(prefix, StringComparison.Ordinal))];
        var counted = CountedSet.Select(rows, data.OtherEpochs).Trades.Where(t => double.IsFinite(t.R)).ToList();
        var perDay = counted.GroupBy(t => IndiaTime.Today(t.EntryAt)).ToDictionary(g => g.Key, g => g.Count());
        return [.. counted.Select(t => new BehaviouralFigure(t, perDay[IndiaTime.Today(t.EntryAt)], IndiaTime.Today(t.ExitAt)))];
    }
}

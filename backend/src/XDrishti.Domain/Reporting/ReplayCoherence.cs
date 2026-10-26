using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Reporting;

/// <summary>The honest state of a replay chart: the bars agree with the stored trade, they do not, or there are none.</summary>
public enum ReplayCoherenceState
{
    /// <summary>The first bar at or after the entry minute opens within one tick of the stored entry price.</summary>
    Ok,

    /// <summary>It does not: the bars changed since the run (a later corporate action, a re-fetch).</summary>
    EntryMismatch,

    /// <summary>No bar in the window.</summary>
    NoBars,
}

/// <summary>The result of <see cref="ReplayCoherence.Check"/>: the state, the stored entry, the price found on the bars (null with none) and a sentence.</summary>
public sealed record ReplayCoherenceResult(ReplayCoherenceState State, double EntryStored, double? EntryOnBars, string Detail);

/// <summary>
/// Whether the bars of a replay still match the stored trade (P8-11, D4). The stored entry is in adjusted prices; the first 1-minute bar at or after the entry minute must open within one tick of it. An entry at a limit may differ from the open, so there
/// the limit must lie inside that bar's range (a tick of slack) instead. Pure.
/// </summary>
public static class ReplayCoherence
{
    /// <summary>Checks the (adjusted, 1-minute, ascending) bars against the stored entry.</summary>
    public static ReplayCoherenceResult Check(IReadOnlyList<Bar> minuteBars, long entryEpoch, double entryPrice, double tick, bool entryAtLimit)
    {
        ArgumentNullException.ThrowIfNull(minuteBars);
        Bar? found = null;
        foreach (var bar in minuteBars)
        {
            if (bar.EpochSeconds >= entryEpoch)
            {
                found = bar;
                break;
            }
        }

        if (found is not { } first)
        {
            return new ReplayCoherenceResult(ReplayCoherenceState.NoBars, entryPrice, null, minuteBars.Count == 0 ? "There are no bars in the window." : "There is no bar at or after the entry minute.");
        }

        var slack = tick + (tick * 1e-6);
        var agrees = entryAtLimit ? entryPrice >= first.Low - slack && entryPrice <= first.High + slack : Math.Abs(first.Open - entryPrice) <= slack;
        return agrees
            ? new ReplayCoherenceResult(ReplayCoherenceState.Ok, entryPrice, entryAtLimit ? entryPrice : first.Open, "The bars agree with the stored entry.")
            : new ReplayCoherenceResult(ReplayCoherenceState.EntryMismatch, entryPrice, first.Open, string.Create(CultureInfo.InvariantCulture, $"The bars no longer match this stored trade: the entry was stored at {entryPrice:0.00##} and the bar at that minute now opens at {first.Open:0.00##} (data changed or a corporate action was applied later)."));
    }
}

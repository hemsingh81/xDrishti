using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The entry-override seam of CF-07 (ADR 0208): a walk whose entry decision is forced at a chosen minute, everything else being the rules of the strategy. The walker (<see cref="TradeWalker"/>) already knows nothing of where a candidate came from
/// (it reads an <see cref="EntryCandidate"/> and the minutes that follow it, strictly forward), so the seam is only the making of the candidate: a forced decision at the end of the minute <c>bars[decisionIndex]</c>, the minute itself as the trigger bar, and
/// a stop at a given level or at a given fraction of the trigger close. A forced entry at the minute and the stop the strategy itself used therefore reproduces the original walk exactly, and a forced entry cannot read a minute after its exit decision.
/// Pure: no random source, no clock, no I/O; the draw of the minute is the caller's.
/// </summary>
public static class EntryOverride
{
    /// <summary>The stop level that is <paramref name="riskFraction"/> of <paramref name="close"/> away from it, below it for a buy and above it for a sell.</summary>
    public static double StopAt(Side side, double close, double riskFraction) => side == Side.Buy ? close * (1 - riskFraction) : close * (1 + riskFraction);

    /// <summary>A decision forced at the end of the minute <c>bars[decisionIndex]</c>: that minute is the evaluation bar (1 minute), its close the trigger close, and <paramref name="stopLevel"/> is used as given.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="bars"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decisionIndex"/> is not a position in <paramref name="bars"/>.</exception>
    public static EntryCandidate Forced(InstrumentRef instrument, string strategyId, IReadOnlyList<Bar> bars, int decisionIndex, Side side, double stopLevel)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentOutOfRangeException.ThrowIfNegative(decisionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(decisionIndex, bars.Count);
        var bar = bars[decisionIndex];
        return new EntryCandidate(strategyId, instrument, side, decisionIndex, bar.EpochSeconds + 60, Timeframe.M1, bar, 1, true, stopLevel, DecisionTrace.Empty);
    }

    /// <summary>Walks the forced decision under the rules of the strategy; the answer is that of the walker, a trade or the reason there is none.</summary>
    public static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkRules rules) => TradeWalker.Walk(candidate, bars, rules);
}

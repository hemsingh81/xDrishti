using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A walk of a seeded day under some fill rules: the candidate, its rules and what the walker gave.</summary>
internal sealed record DayWalk(SeededDay Day, SeededWalk Walk, WalkOutcome Outcome)
{
    public SimTrade? Trade => Outcome.Trade;
}

/// <summary>The helpers of the properties over the seeded days (criteria 18 to 21): walking, the index at which an outcome is settled, the doctored inputs of the negative controls.</summary>
internal static class WalkProperties
{
    /// <summary>The doc 16 defaults for the ladder's fixed parameters.</summary>
    public static FillRules Default => FillRules.Default;

    public static FillRules Rules(double ticks, double barRangeFrac, double stopExitExtraTicks) =>
        FillRules.Create(0.1, SlippageConfig.Create(ticks, barRangeFrac, stopExitExtraTicks).Value).Value;

    public static Result<WalkOutcome> Run(SeededWalk walk, IReadOnlyList<Bar> bars, FillRules fills) =>
        TradeWalker.Walk(walk.Candidate, bars, walk.Rules with { Fills = fills });

    public static WalkOutcome Ok(Result<WalkOutcome> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        return result.Value;
    }

    /// <summary>Every walk of every day, under the fill rules.</summary>
    public static List<DayWalk> WalkAll(IEnumerable<SeededDay> days, FillRules fills) =>
        [.. days.SelectMany(day => day.Walks.Select(walk => new DayWalk(day, walk, Ok(Run(walk, day.Bars, fills)))))];

    /// <summary>
    /// The index of the last minute the walk of the outcome had to read. A trade is settled at its exit minute, except an end-of-data exit, which is known only when the date ends (so: the last minute of the date); a no-trade
    /// at the minute that decided it, found here by the test's own scan: the first minute after the candidate's that holds a trade, or the first that is on another date or too late, or the end of the list.
    /// </summary>
    public static int SettledAt(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkOutcome outcome)
    {
        if (outcome.Trade is { } trade)
        {
            var exit = IndexOf(bars, trade.ExitEpochSeconds, candidate.DecisionBarIndex);
            if (trade.ExitReason != ExitReason.EndOfData)
            {
                return exit;
            }

            var date = SessionClock.DateOf(bars[exit].EpochSeconds);
            while (exit + 1 < bars.Count && SessionClock.DateOf(bars[exit + 1].EpochSeconds) == date)
            {
                exit++;
            }

            return exit;
        }

        var own = bars[candidate.DecisionBarIndex];
        for (var j = candidate.DecisionBarIndex + 1; j < bars.Count; j++)
        {
            var bar = bars[j];
            if (SessionClock.DateOf(bar.EpochSeconds) != SessionClock.DateOf(own.EpochSeconds)
                || bar.EpochSeconds - candidate.DecisionEpochSeconds > SimulationEngine.MaxFillDelayMinutes * 60L
                || bar.Volume > 0)
            {
                return j;
            }
        }

        return bars.Count - 1;
    }

    public static int IndexOf(IReadOnlyList<Bar> bars, long epoch, int from)
    {
        for (var i = from; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds == epoch)
            {
                return i;
            }
        }

        throw new InvalidOperationException("The minute is not in the list.");
    }

    /// <summary>The bars after the cut point <paramref name="k"/> replaced by zero, huge and mixed extreme prices and volumes (with and without volume); the epochs stay.</summary>
    public static IReadOnlyList<Bar> ReplaceAfter(IReadOnlyList<Bar> bars, int k)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            result[i] = i <= k ? bars[i] : (i % 3) switch
            {
                0 => new Bar(bars[i].EpochSeconds, 0, 0, 0, 0, 0),
                1 => new Bar(bars[i].EpochSeconds, 1e15, 1e15, 1e15, 1e15, long.MaxValue),
                _ => new Bar(bars[i].EpochSeconds, 0, 1e15, 0, 1e15, long.MaxValue),
            };
        }

        return result;
    }

    /// <summary>
    /// The peek control (criterion 20c, i): every minute's low (a sell: high) becomes the lower (higher) of its own and the next minute's, so that a stop or target test on this list looks one minute ahead. A doctored input and
    /// not a mutated walker: the walker is a static function.
    /// </summary>
    public static IReadOnlyList<Bar> Peek(IReadOnlyList<Bar> bars, Side side)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var next = i + 1 < bars.Count ? bars[i + 1] : bars[i];
            result[i] = side == Side.Buy ? bars[i] with { Low = Math.Min(bars[i].Low, next.Low) } : bars[i] with { High = Math.Max(bars[i].High, next.High) };
        }

        return result;
    }

    public static IReadOnlyList<Bar> Identity(IReadOnlyList<Bar> bars, Side side) => bars;

    /// <summary>The signal-minute control (criterion 20c, ii): the candidate decided one minute earlier, with the decision instant set to the end of that earlier minute so that the consistency check does not fire.</summary>
    public static EntryCandidate OneMinuteEarlier(EntryCandidate candidate, IReadOnlyList<Bar> bars) =>
        candidate with { DecisionBarIndex = candidate.DecisionBarIndex - 1, DecisionEpochSeconds = bars[candidate.DecisionBarIndex - 1].EpochSeconds + 60 };

    /// <summary>Numbers as the determinism tests print them: invariant culture, round-trip.</summary>
    public static string Render(Result<WalkOutcome> result) => OutcomeFormatter.Render(result);
}

using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A walk of one candidate of a seeded swing series, on its own (no gate).</summary>
internal sealed record SwingWalkResult(SwingSeries Series, EntryCandidate Candidate, Result<WalkOutcome> Result)
{
    public WalkOutcome Outcome => Result.Value;

    public SimTrade? Trade => Result.IsSuccess ? Result.Value.Trade : null;
}

/// <summary>The helpers of the swing properties: walking, the index at which an outcome is settled, the doctored inputs and the wrappers of the negative controls.</summary>
internal static class SwingWalks
{
    /// <summary>Every candidate of every series, walked on its own with the calendar the series gives the walker.</summary>
    public static List<SwingWalkResult> WalkAll(IEnumerable<SwingSeries> series, FillRules fills) =>
        [.. series.SelectMany(s => s.Candidates.Select(c => new SwingWalkResult(s, c, TradeWalker.Walk(c, s.Bars, s.Rules(fills)))))];

    /// <summary>The candidates of the series through the position gate with the real walker.</summary>
    public static IReadOnlyList<CandidateOutcome> Gated(SwingSeries series, FillRules fills)
    {
        var rules = series.Rules(fills);
        var result = PositionGate.Apply(series.Candidates, c => TradeWalker.Walk(c, series.Bars, rules));
        result.IsSuccess.ShouldBeTrue("seed " + series.Seed + ": " + (result.IsFailure ? result.Error.Code : string.Empty));
        return result.Value;
    }

    /// <summary>The text of a walk, as the golden files print it.</summary>
    public static string Text(Result<WalkOutcome> result) => OutcomeFormatter.Render(result);

    /// <summary>
    /// The index of the last minute the walk had to read (D14, criterion 21): the exit minute of a trade; for an end-of-data trade the end of the list; for a <c>MaxHold</c> at the last close the first minute that revealed it (the first on a
    /// later date than the due session, or on it after the due minute); for a no-trade the minute that decided it (the test's own scan, shared with the intraday properties).
    /// </summary>
    public static int SettledAt(SwingSeries series, EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkOutcome outcome)
    {
        if (outcome.Trade is not { } trade)
        {
            return WalkProperties.SettledAt(candidate, bars, outcome);
        }

        var exit = WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, candidate.DecisionBarIndex);
        if (trade.ExitReason == ExitReason.EndOfData)
        {
            return bars.Count - 1;
        }

        if (!outcome.MaxHoldAtLastClose)
        {
            return exit;
        }

        var due = series.Due(trade.TradeDate);
        var dueMinute = series.DueMinute(due);
        for (var i = exit + 1; i < bars.Count; i++)
        {
            var date = SessionClock.DateOf(bars[i].EpochSeconds);
            if (date > due || (date == due && SessionClock.MinuteOfDay(bars[i].EpochSeconds) > dueMinute))
            {
                return i;
            }
        }

        throw new InvalidOperationException("A MaxHold at the last close has no minute that revealed it.");
    }

    /// <summary>
    /// The peek control (criterion 21b): the last minute of every session is given the lower of its own low and the next session's first open, and the higher of the highs, so that a stop or a target could "see" tomorrow's gap. A doctored
    /// input and not a mutated walker: the walker is a static function.
    /// </summary>
    public static IReadOnlyList<Bar> PeekNextSession(IReadOnlyList<Bar> bars)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var isLast = i + 1 < bars.Count && SessionClock.DateOf(bars[i + 1].EpochSeconds) != SessionClock.DateOf(bars[i].EpochSeconds);
            result[i] = isLast ? bars[i] with { Low = Math.Min(bars[i].Low, bars[i + 1].Open), High = Math.Max(bars[i].High, bars[i + 1].Open) } : bars[i];
        }

        return result;
    }

    /// <summary>
    /// The calendar-from-the-future control (criterion 21b): the real walker given a calendar that is read off the list, in which a date is a session only when some later date follows it in the list. The hold then depends on what comes after
    /// the minutes it has read: a list cut at the due session has no session to hold to.
    /// </summary>
    public static Result<WalkOutcome> WalkWithCalendarFromTheFuture(SwingSeries series, EntryCandidate candidate, IReadOnlyList<Bar> bars, FillRules fills)
    {
        var dates = bars.Select(b => SessionClock.DateOf(b.EpochSeconds)).Distinct().Order().ToList();
        var counted = dates.Take(Math.Max(0, dates.Count - 1)).ToHashSet();
        var holidays = new List<DateOnly>();
        var specials = new List<(DateOnly, int, int)>();
        for (var date = dates[0]; date <= dates[0].AddDays(60); date = date.AddDays(1))
        {
            var weekday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            if (weekday && !counted.Contains(date))
            {
                holidays.Add(date);
            }
            else if (!weekday && counted.Contains(date))
            {
                specials.Add((date, SessionClock.OpenMinute, SessionClock.CloseMinute));
            }
        }

        var calendar = TestCalendars.Of(holidays, specials);
        return TradeWalker.Walk(candidate, bars, series.Rules(fills, calendar));
    }
}

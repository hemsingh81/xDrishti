using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The ticket after a daily bar of P3-03e (rows O1 to O9): Monday's daily bar <c>O 98.00 H 101.00 L 97.50 C 100.00</c> closes the evaluation at 15:30 (decision instant Monday 15:30, the candidate's own minute 15:29), trigger close 100.00,
/// limit <c>tickDown(100.10)</c> = 100.10, stop 95.00, <c>FIXED_RR 2.0</c>, a hold of 2 sessions, the default week, tick 0.05. The scanner is not involved: the candidate is made by hand.
/// </summary>
internal static class OvernightRows
{
    public const double Stop = 95.00;

    /// <summary>Monday: starts at 98.00, <c>10:00 98.00/101.00/97.50/100.00</c>, flat 100.00 after (the daily bar of the rows).</summary>
    public static Day Monday(DateOnly? date = null) => Day.Dense(date ?? Mon).Start(98.00).Set("10:00 98.00/101.00/97.50/100.00");

    /// <summary>The first day after Monday as O1 gives it: <c>09:15 100.05/100.40/99.90/100.30</c>, flat 100.30 after.</summary>
    public static Day Fill(DateOnly date, string first = "09:15 100.05/100.40/99.90/100.30") => Day.Dense(date).Start(100.30).Set(first);

    /// <summary>The candidate decided at 15:30 on <paramref name="date"/>, whose own minute (15:29) is in <paramref name="bars"/>.</summary>
    public static EntryCandidate Candidate(IReadOnlyList<Bar> bars, DateOnly? date = null, double stop = Stop, double close = 100.00)
    {
        var day = date ?? Mon;
        var index = bars.ToList().FindIndex(b => b.EpochSeconds == Day.Epoch(day, Day.Minute("15:29")));
        index.ShouldBeGreaterThanOrEqualTo(0, "the candidate's own minute must be in the list");
        var daily = new Bar(Day.Epoch(day, Day.Minute("09:15")), 98.00, 101.00, 97.50, close, 375_000);
        return new EntryCandidate("SWING_DAILY_TEST", Cand.Test, Side.Buy, index, Day.Epoch(day, Day.Minute("15:30")), Timeframe.D1, daily, 375, TriggerBarComplete: true, stop, DecisionTrace.Empty);
    }

    public static WalkRules Rules(FillRules fills, int sessions = 2, TradingCalendar? calendar = null, double rewardRisk = 2.0) =>
        new(rewardRisk, WalkRules.NoSquareOff, fills, Cand.Tick05, SwingHold.Create(sessions, calendar).Value);

    /// <summary>Walks the candidate of Monday (or of <paramref name="date"/>) over the list.</summary>
    public static WalkOutcome Walk(IReadOnlyList<Bar> bars, FillRules fills, DateOnly? date = null, int sessions = 2, TradingCalendar? calendar = null, double rewardRisk = 2.0)
    {
        var result = TradeWalker.Walk(Candidate(bars, date), bars, Rules(fills, sessions, calendar, rewardRisk));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    /// <summary>O1: Monday, Tuesday as <see cref="Fill"/>, Wednesday starts at 100.30 and ends with <c>15:29 100.55/100.70/100.50/100.60</c>.</summary>
    public static Bar[] O1() => Day.Join(Monday(), Fill(Tue), Day.Dense(Wed).Start(100.30).Set("15:29 100.55/100.70/100.50/100.60"));

    public static decimal Q(double value) => Math.Round((decimal)value, 4);
}

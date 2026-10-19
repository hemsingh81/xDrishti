using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// The transformations and the digest of the added leak properties of P3-08b. A property here is "an input moved in a way that must change nothing the rules can see, and the output moved back is the output": the digest writes every
/// field of a candidate, its walk and the counters, with prices divided by <c>priceDivisor</c> (a power of two, so the division is exact), epochs and dates moved back by the shift, and volumes divided by <c>volumeDivisor</c>.
/// </summary>
internal static class Leak
{
    public const long SecondsPerWeek = 7 * 86_400L;

    /// <summary>The bars with every price and every volume multiplied by <paramref name="factor"/> (a power of two keeps every double exact).</summary>
    public static Bar[] Scale(IReadOnlyList<Bar> bars, int factor) =>
        [.. bars.Select(b => new Bar(b.EpochSeconds, b.Open * factor, b.High * factor, b.Low * factor, b.Close * factor, b.Volume * factor))];

    /// <summary>The bars moved forward by whole weeks (the same weekdays).</summary>
    public static Bar[] Shift(IReadOnlyList<Bar> bars, int weeks) =>
        [.. bars.Select(b => b with { EpochSeconds = b.EpochSeconds + (weeks * SecondsPerWeek) })];

    public static TickSpec Tick(decimal rupees) => TickSpec.FromRupees(rupees).Value;

    /// <summary>
    /// The calendar moved forward by whole weeks: every exception (a holiday on a weekday, a session other than the regular one) of the dates from the first bar to ten days after the last, written again <paramref name="weeks"/> later. A calendar lists
    /// no days, so this asks <c>SessionOf</c> for each date, as the run record does.
    /// </summary>
    public static TradingCalendar ShiftCalendar(TradingCalendar calendar, IReadOnlyList<Bar> bars, int weeks)
    {
        var first = SessionClock.DateOf(bars[0].EpochSeconds);
        var last = SessionClock.DateOf(bars[^1].EpochSeconds).AddDays(10);
        var holidays = new List<DateOnly>();
        var specials = new List<(DateOnly Date, int From, int To)>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            var weekday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var session = calendar.SessionOf(date);
            if (session is null)
            {
                if (weekday)
                {
                    holidays.Add(date.AddDays(7 * weeks));
                }
            }
            else if (!weekday || !session.Value.IsRegular)
            {
                specials.Add((date.AddDays(7 * weeks), session.Value.FromMinute, session.Value.ToMinute));
            }
        }

        return TestCalendars.Of(holidays, specials);
    }

    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string N(double? value) => value is { } v ? N(v) : "null";

    /// <summary>Every field of every outcome (candidate, trade, flags) and the counters of both stages, as moved back to the frame of the base run.</summary>
    public static string Digest(SimulationResult result, double priceDivisor = 1, long volumeDivisor = 1, int shiftWeeks = 0)
    {
        var text = new StringBuilder();
        foreach (var o in result.Outcomes)
        {
            text.Append(Line(o, priceDivisor, volumeDivisor, shiftWeeks)).Append('\n');
        }

        foreach (var scan in result.Scans)
        {
            text.Append(Render.Counters(scan)).Append('\n');
        }

        return text.Append(OutcomeFormatter.Render(result.Walk)).ToString();
    }

    /// <summary>The candidate of an outcome, with its trace.</summary>
    public static string CandidateLine(CandidateOutcome o, double priceDivisor = 1, long volumeDivisor = 1, int shiftWeeks = 0) =>
        Line(o, priceDivisor, volumeDivisor, shiftWeeks).Split(" -> ", 2)[0];

    /// <summary>One outcome as text: the candidate with its trace, then the trade or the reason, then the flags of the walk.</summary>
    public static string Line(CandidateOutcome o, double priceDivisor = 1, long volumeDivisor = 1, int shiftWeeks = 0)
    {
        var seconds = shiftWeeks * SecondsPerWeek;
        string P(double v) => N(v / priceDivisor);
        string P2(double? v) => v is { } x ? P(x) : "null";
        var text = new StringBuilder();
        var c = o.Candidate;
        text.Append(CultureInfo.InvariantCulture, $"cand {c.Side} i={c.DecisionBarIndex} d={c.DecisionEpochSeconds - seconds} tf={c.TriggerTimeframe} stop={P(c.StopLevel)} ")
            .Append(CultureInfo.InvariantCulture, $"bar={c.TriggerBar.EpochSeconds - seconds}/{P(c.TriggerBar.Open)}/{P(c.TriggerBar.High)}/{P(c.TriggerBar.Low)}/{P(c.TriggerBar.Close)}/{c.TriggerBar.Volume / volumeDivisor}/{c.TriggerBarMinutes}/{c.TriggerBarComplete}");
        foreach (var item in c.Trace.Items)
        {
            text.Append(CultureInfo.InvariantCulture, $" [{item.Path} {item.Source} {item.Truth}]");
        }

        var w = o.Walk;
        if (w.Trade is { } t)
        {
            text.Append(CultureInfo.InvariantCulture, $" -> trade {t.Side} date={t.TradeDate.AddDays(-7 * shiftWeeks):yyyy-MM-dd} dec={t.DecisionEpochSeconds - seconds} entry={t.EntryEpochSeconds - seconds}@{P(t.EntryPrice)} stop={P(t.StopPrice)} target={P2(t.TargetPrice)} ")
                .Append(CultureInfo.InvariantCulture, $"exit={t.ExitEpochSeconds - seconds}@{P(t.ExitPrice)} {t.ExitReason} R={N(t.RExact)} mae={N(t.MaeR)} mfe={N(t.MfeR)} rf={N(t.RFilled)} slip={N(t.SlippageR)} ")
                .Append(CultureInfo.InvariantCulture, $"ef={P(t.EntryFilledPrice)} xf={P(t.ExitFilledPrice)} {t.EntryFill} fb={t.TickFallback} held={t.SessionsHeld} unseen={t.SessionsUnseen}");
        }
        else
        {
            text.Append(" -> no-trade ").Append(w.NoTrade);
        }

        text.Append(CultureInfo.InvariantCulture, $" | ex={w.BarsExamined} {w.EntryAfterMissingMinutes} {w.StopGapExit} {w.TargetGapExit} {w.AmbiguousMinute} {w.LateSquareOff} {w.RiskUnderFourTicks} z={w.ZeroVolumeMinutes} {w.MaxHoldAtLastClose} {w.OvernightStopGap} {w.OvernightTargetGap} {w.OvernightEntry}");
        if (w.MaxHoldAtLastClose)
        {
            text.Append(CultureInfo.InvariantCulture, $" holdEnd={w.HoldEndEpochSeconds - seconds}");
        }

        return text.ToString();
    }
}

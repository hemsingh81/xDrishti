using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>
/// The machine check of one spot-check line (spec P3-09d D4), pure. It checks, from the stored bars of the day and the trades of the pack, that the day is what the run record says it is under the rules of the replay (ADR 0037): a short session has fewer than
/// <see cref="Completeness"/> of the 375 regular minutes, a weekend date is not a regular session, a missing session has no stored bar, a flagged day opens more than <see cref="Threshold"/> from the previous close, and no trade of the pack lies on an excluded day.
/// What the bars alone cannot show is returned as <see cref="SpotOutcome.NotMachineCheckable"/> with the reason, never as a pass.
/// </summary>
internal static class SpotRules
{
    /// <summary>The registered defaults of the replay, which <c>verify-pack</c> runs with.</summary>
    private static readonly ReplayOptions Defaults = new();

    public static decimal Completeness => (decimal)Defaults.MinSessionCompleteness;

    public static decimal Threshold => (decimal)Defaults.DiscontinuityThreshold;

    public static SpotResult Evaluate(SpotLine spot, SpotDay day, bool packHasTradeThatDay)
    {
        ArgumentNullException.ThrowIfNull(spot);
        ArgumentNullException.ThrowIfNull(day);

        var inWindow = day.Bars.Where(b => SessionClock.InRegularSession(SessionClock.MinuteOfDay(b.EpochSeconds))).OrderBy(b => b.EpochSeconds).ToList();
        var weekend = spot.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var required = Completeness * SessionClock.RegularBars;
        var count = inWindow.Count.ToString(CultureInfo.InvariantCulture);

        if (spot.Kind == "excluded_session" && packHasTradeThatDay)
        {
            return Of(spot, SpotOutcome.Contradicted, "a trade of the pack is dated on this day", "an excluded session must carry no trade");
        }

        const string NoTrade = "; no trade of the pack is dated on the day";
        return spot.Kind switch
        {
            "excluded_session" when spot.Detail == "short_session" => inWindow.Count < required
                ? Of(spot, SpotOutcome.Confirmed, $"stored bars in the regular window: {count} of {SessionClock.RegularBars}, below {Completeness:0.##} of them" + NoTrade, "why the minutes are missing (a real short day or a gap in the data)")
                : Of(spot, SpotOutcome.Contradicted, $"stored bars in the regular window: {count} of {SessionClock.RegularBars}, not below {Completeness:0.##} of them: the replay would have run this day (or bars were added after the run)", string.Empty),
            "excluded_session" when spot.Detail == "special_session" => weekend
                ? Of(spot, SpotOutcome.Confirmed, "the date is a Saturday or Sunday, never a regular session" + NoTrade, "that the exchange held a special session on it (the trading calendar is in the database)")
                : Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, "a special session on a weekday (an evening or shortened session) is declared in the trading calendar in the database; the stored bars of the regular window cannot show it"),
            "excluded_session" when spot.Detail == "missing_session" => weekend
                ? Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, "a missing session is a calendar trading day with no bar; this date is a weekend and the calendar is in the database")
                : day.Bars.Count == 0
                    ? Of(spot, SpotOutcome.Confirmed, "no stored bar on this weekday" + NoTrade, "that the weekday is a trading day (the calendar is in the database)")
                    : Of(spot, SpotOutcome.Contradicted, $"{day.Bars.Count.ToString(CultureInfo.InvariantCulture)} stored bars exist on a day the run record calls missing (bars were added after the run, or the record is wrong)", string.Empty),
            "flagged_day" => Flagged(spot, inWindow.Count == 0 ? null : inWindow[0], day.PreviousLast),
            "open_at_end" => Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, "an open position is a fact of the run record, which is not in the pack folder; the stored bars alone do not say where the data ends for the walk"),
            _ => Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, "an unknown kind of spot line"),
        };
    }

    private static SpotResult Flagged(SpotLine spot, ReaderBar? first, ReaderBar? previous)
    {
        if (first is null || previous is null || previous.Close <= 0m)
        {
            return Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, "the day or a previous session (within " + GateRunner.LookbackDays.ToString(CultureInfo.InvariantCulture) + " days) has no stored bar in the regular window, so the jump cannot be computed");
        }

        var jump = Math.Abs(first.Open - previous.Close) / previous.Close;
        return jump > Threshold
            ? Of(spot, SpotOutcome.Confirmed, $"first open {first.Open.ToString("0.####", CultureInfo.InvariantCulture)} against the previous close {previous.Close.ToString("0.####", CultureInfo.InvariantCulture)}: a jump of {(jump * 100m).ToString("0.##", CultureInfo.InvariantCulture)} %, above {(Threshold * 100m).ToString("0.##", CultureInfo.InvariantCulture)} %", "the corporate-action adjustment of the replay's series (confirmed means only that the raw stored jump exceeds the threshold)")
            : Of(spot, SpotOutcome.NotMachineCheckable, string.Empty, $"the stored bars show a jump of {(jump * 100m).ToString("0.##", CultureInfo.InvariantCulture)} %, not above {(Threshold * 100m).ToString("0.##", CultureInfo.InvariantCulture)} %; the replay tests adjusted prices (corporate actions applied), which are not read here, so the flag cannot be confirmed or refuted from the stored bars");
    }

    private static SpotResult Of(SpotLine spot, SpotOutcome outcome, string done, string notDone) => new(spot, outcome, done, notDone);
}

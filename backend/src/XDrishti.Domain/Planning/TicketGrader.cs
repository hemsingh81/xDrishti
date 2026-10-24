using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Turns the simulator's record of one instrument into the grade of a ticket (P7-13, ADR 0090), where F is the planned session and D the session the ticket was built on (the last session before F). Pure.
/// <list type="bullet">
/// <item><b>Which decision is the ticket's.</b> The engine cuts trades by the day of the DECISION (ADR 0043 D6). A ticket whose trigger is on a daily-or-longer timeframe is decided at the close of D (known at that close, filled at the open of
/// F; ADR 0088 section 2) and ONLY that decision counts: a daily decision on F is made at the close of F and belongs to the ticket for the next session. A ticket with an intraday trigger is decided on F and ONLY that decision counts; an
/// intraday decision on D used information the ticket did not have. The first qualifying trade by entry instant is the outcome.</item>
/// <item><b>What can be attributed to the decision day, and how.</b> The replay starts at the first stored session whatever its window is, and only some counters are cut to the window. The cut ones (a trade dropped by the sizing or cost rule,
/// a swing position that cannot be followed across a price discontinuity) are attributed by taking away a second run that starts the day after the decision day (<c>tail</c>, same end). The others (flagged days, every no-trade reason)
/// cover the whole history and are NOT used as counters: a flagged decision day or fill day is read from the run's dated list of flagged days, an excluded session from its dated list of notes, and a no-trade ticket carries one generic
/// reason. Nothing from another day can decide this ticket's status.</item>
/// <item>R is the net R of the charge sheet when the trade was charged, otherwise the R from the filled prices (the convention of the cells).</item>
/// <item><b>Pending is not a grade.</b> No bar of F yet, a position still open at the end of the data, a refused or skipped replay (main or tail), a flagged decision or fill day, an excluded session, or data that ends on the decision day of a
/// ticket whose order may rest overnight leave the ticket for a later run. Only a deterministic, final fact is stored as not gradable.</item>
/// </list>
/// </summary>
public static class TicketGrader
{
    /// <summary>
    /// The grade of a ticket for <paramref name="side"/> planned for <paramref name="forDate"/> and built on <paramref name="informationDate"/>. <paramref name="main"/> is the run from the decision day (D when <paramref name="daily"/>, else F) to the end
    /// of the read; <paramref name="tail"/> is the run from the day after the decision day to the same end, or null when that day is after the end of the read. <paramref name="restsOvernight"/>: the ticket's style is swing, so an order may rest
    /// past the decision session.
    /// </summary>
    public static GraderResult Grade(SimulationRun main, SimulationRun? tail, Side side, DateOnly forDate, DateOnly informationDate, bool daily, bool restsOvernight)
    {
        ArgumentNullException.ThrowIfNull(main);
        var decisionDay = daily ? informationDate : forDate;
        var gross = "gross, no charges";
        var basis = main.Costs is { } costs ? "net of the charge sheet " + costs.SheetId : gross;
        if (main.Instruments.Count != 1 || (tail is not null && tail.Instruments.Count != 1))
        {
            return Pending("the replay did not cover exactly one instrument");
        }

        var inst = main.Instruments[0];
        if (inst.LastBar is null || inst.LastBar < forDate)
        {
            return Pending("the stored bars do not yet reach " + Date(forDate));
        }

        foreach (var run in new[] { inst }.Concat(tail is null ? [] : [tail.Instruments[0]]))
        {
            if (run.Skipped is { } skip)
            {
                return Pending("the replay skipped the instrument: " + skip.ToCode() + (run.SkipDetail is { } d ? " (" + d + ")" : string.Empty));
            }
        }

        if (inst.Notes.FirstOrDefault(n => n.Date == decisionDay || n.Date == forDate) is { } excluded)
        {
            return Pending("the session " + Date(excluded.Date) + " is left out of the replay (" + excluded.Reason.ToCode() + ")");
        }

        if (inst.Discontinuities.Any(n => n.Date == decisionDay || n.Date == forDate))
        {
            return Pending("the decision day or the fill day is a flagged day (price discontinuity); the replay cannot follow it");
        }

        bool Mine(SimTrade t) => t.Side == side && SessionClock.DateOf(t.DecisionEpochSeconds - 60) == decisionDay;
        var mine = main.Trades.Where(t => Mine(t.Trade)).OrderBy(t => t.Trade.EntryEpochSeconds).ToList();
        if (mine.Count > 0)
        {
            var t = mine[0].Trade;
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            var r = costed ? t.Costing!.RNet : t.RFilled;
            var detail = string.Create(CultureInfo.InvariantCulture, $"entered {Date(t.TradeDate)}, left {Date(SessionClock.DateOf(t.ExitEpochSeconds))} ({t.ExitReason})");
            return new GraderResult(new TicketGrade(GradeStatus.Graded, r, t.MaeR, t.MfeR, t.ExitReason.ToString(), t.EntryPrice, t.ExitPrice, costed ? basis : gross, detail, t.StopPrice), null);
        }

        if (main.OpenAtEnd.Any(t => Mine(t.Trade)))
        {
            return Pending("the position is still open at the end of the stored bars");
        }

        // The counters that are cut to the window by the day of the decision: the window of the main run starts on the decision day, the tail's on the day after, so the difference is the decision day alone.
        var f = main.Totals.Fates;
        var t0 = tail?.Totals.Fates;
        var excludedAcross = f.ExcludedAcrossDiscontinuity - (t0?.ExcludedAcrossDiscontinuity ?? 0);
        var dropped = (f.SkippedQuantity + f.SkippedCost) - (t0 is null ? 0 : t0.SkippedQuantity + t0.SkippedCost);
        if (excludedAcross > 0)
        {
            return Pending("a swing position of the decision day cannot be followed across a price discontinuity");
        }

        if (dropped > 0)
        {
            return Done(GradeStatus.NotGradable, basis, "the replay dropped the trade of the decision day: the sizing or cost rule refused it");
        }

        if (restsOvernight && inst.LastBar <= decisionDay)
        {
            return Pending("the stored bars end on the decision day; an order resting overnight cannot yet be told from none");
        }

        var day = daily ? "the decision at the close of " + Date(informationDate) : "the decision on " + Date(forDate);
        return Done(GradeStatus.NotTriggered, basis, "no entry was taken for " + day + ": the entry condition did not hold or the order did not fill (the replay's no-trade reasons cover its whole history and are not attributed to one day)");
    }

    /// <summary>A grade for a ticket that cannot be replayed, for a final reason.</summary>
    public static TicketGrade NotGradable(string reason) => new(GradeStatus.NotGradable, null, null, null, null, null, null, "none", reason);

    private static GraderResult Pending(string reason) => new(null, reason);

    private static GraderResult Done(GradeStatus status, string basis, string detail) => new(new TicketGrade(status, null, null, null, null, null, null, basis, detail), null);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

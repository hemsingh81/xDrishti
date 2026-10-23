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
/// <item><b>Reasons come only from the ticket's own decision day.</b> The run's fates cover every day of its window, so the fates of the days after the decision day are taken away with the fates of a second run that starts the day after
/// (<c>tail</c>, same end): a dropped trade, a flagged day or an open position on another day never decides this ticket's status.</item>
/// <item>R is the net R of the charge sheet when the trade was charged, otherwise the R from the filled prices (the convention of the cells).</item>
/// <item><b>Pending is not a grade.</b> No bar of F yet, a position still open at the end of the data, a refused or skipped replay, a flagged day or a pending order of the decision day leave the ticket for a later run. Only a deterministic, final fact is stored as not gradable.</item>
/// </list>
/// </summary>
public static class TicketGrader
{
    /// <summary>
    /// The grade of a ticket for <paramref name="side"/> planned for <paramref name="forDate"/> and built on <paramref name="informationDate"/>. <paramref name="main"/> is the run from the decision day (D when <paramref name="daily"/>, else F) to the end
    /// of the read; <paramref name="tail"/> is the run from the day after the decision day to the same end, or null when that day is after the end of the read.
    /// </summary>
    public static GraderResult Grade(SimulationRun main, SimulationRun? tail, Side side, DateOnly forDate, DateOnly informationDate, bool daily)
    {
        ArgumentNullException.ThrowIfNull(main);
        var run = main;
        var decisionDay = daily ? informationDate : forDate;
        var gross = "gross, no charges";
        var basis = run.Costs is { } costs ? "net of the charge sheet " + costs.SheetId : gross;
        if (run.Instruments.Count != 1 || (tail is not null && tail.Instruments.Count != 1))
        {
            return Pending("the replay did not cover exactly one instrument");
        }

        var inst = run.Instruments[0];
        if (inst.LastBar is null || inst.LastBar < forDate)
        {
            return Pending("the stored bars do not yet reach " + Date(forDate));
        }

        if (inst.Skipped is { } skip)
        {
            return Pending("the replay skipped the instrument: " + skip.ToCode() + (inst.SkipDetail is { } d ? " (" + d + ")" : string.Empty));
        }

        bool Mine(SimTrade t) => t.Side == side && SessionClock.DateOf(t.DecisionEpochSeconds - 60) == decisionDay;
        var mine = run.Trades.Where(t => Mine(t.Trade)).OrderBy(t => t.Trade.EntryEpochSeconds).ToList();
        if (mine.Count > 0)
        {
            var t = mine[0].Trade;
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            var r = costed ? t.Costing!.RNet : t.RFilled;
            var detail = string.Create(CultureInfo.InvariantCulture, $"entered {Date(t.TradeDate)}, left {Date(SessionClock.DateOf(t.ExitEpochSeconds))} ({t.ExitReason})");
            return new GraderResult(new TicketGrade(GradeStatus.Graded, r, t.MaeR, t.MfeR, t.ExitReason.ToString(), t.EntryPrice, t.ExitPrice, costed ? basis : gross, detail), null);
        }

        if (run.OpenAtEnd.Any(t => Mine(t.Trade)))
        {
            return Pending("the position is still open at the end of the stored bars");
        }

        var f = tail is null ? run.Totals.Fates : run.Totals.Fates.Minus(tail.Totals.Fates);
        if (f.EntriesSuppressed + f.ExcludedAcrossDiscontinuity > 0)
        {
            return Pending("the decision day is a flagged day (price discontinuity); the replay cannot follow it");
        }

        if (f.OrderPendingAtEnd > 0)
        {
            return Pending("an entry order was still pending at the end of the stored bars");
        }

        if (f.SkippedQuantity + f.SkippedCost > 0)
        {
            return Done(GradeStatus.NotGradable, basis, "the replay dropped the trade: the sizing or cost rule refused it");
        }

        var why = f.LimitNotReached > 0 ? "the entry limit was not reached"
            : f.NoFillBar > 0 ? "no bar to fill the entry"
            : f.EntryThroughStop > 0 ? "the price opened through the stop"
            : f.AfterSquareOff > 0 ? "the signal came after the square-off time"
            : f.PositionOpen > 0 ? "an earlier position in the replay was still open (one position at a time; not the owner's book)"
            : daily ? "the entry condition did not hold at the close of " + Date(informationDate) : "the entry condition did not hold on " + Date(forDate);
        return Done(GradeStatus.NotTriggered, basis, why);
    }

    /// <summary>A grade for a ticket that cannot be replayed, for a final reason.</summary>
    public static TicketGrade NotGradable(string reason) => new(GradeStatus.NotGradable, null, null, null, null, null, null, "none", reason);

    private static GraderResult Pending(string reason) => new(null, reason);

    private static GraderResult Done(GradeStatus status, string basis, string detail) => new(new TicketGrade(status, null, null, null, null, null, null, basis, detail), null);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

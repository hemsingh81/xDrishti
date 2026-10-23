using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Turns the simulator's record of one instrument over the window [D, read-to] into the grade of a ticket (P7-13, ADR 0090), where F is the planned session and D the session the ticket was built on (the last session before F). Pure.
/// <list type="bullet">
/// <item><b>Which trade counts.</b> The engine cuts trades by the day of the DECISION (ADR 0043 D6), so the window starts on D. A trade belongs to the ticket when it was decided on F (an intraday signal, or a daily one that fills on the next
/// session), or decided at the close of D on a daily-or-longer trigger (known at the close of D, filled at the open of F; ADR 0088 section 2). A trade decided before the close of D (an intraday trigger on D) used information the ticket
/// did not have and is never graded. The first such trade by entry instant is the outcome.</item>
/// <item>R is the net R of the charge sheet when the trade was charged, otherwise the R from the filled prices (the convention of the cells).</item>
/// <item><b>Pending is not a grade.</b> No bar of F yet, a position still open at the end of the data, a refused or skipped replay, a flagged day or a pending order leave the ticket for a later run. Only a deterministic, final fact is stored as not gradable.</item>
/// </list>
/// </summary>
public static class TicketGrader
{
    /// <summary>The grade of a ticket for <paramref name="side"/> planned for <paramref name="forDate"/> and built on <paramref name="informationDate"/>, from a run over one instrument.</summary>
    public static GraderResult Grade(SimulationRun run, Side side, DateOnly forDate, DateOnly informationDate)
    {
        ArgumentNullException.ThrowIfNull(run);
        var gross = "gross, no charges";
        var basis = run.Costs is { } costs ? "net of the charge sheet " + costs.SheetId : gross;
        if (run.Instruments.Count != 1)
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

        bool Mine(SimTrade t) => t.Side == side && t.TradeDate >= forDate && Belongs(t, forDate, informationDate);
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

        var f = run.Totals.Fates;
        if (f.EntriesSuppressed + f.ExcludedAcrossDiscontinuity > 0)
        {
            return Pending("a flagged day (price discontinuity) lies in the window; the replay cannot follow it");
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
            : "the entry condition did not hold on " + Date(forDate);
        return Done(GradeStatus.NotTriggered, basis, why);
    }

    /// <summary>A grade for a ticket that cannot be replayed, for a final reason.</summary>
    public static TicketGrade NotGradable(string reason) => new(GradeStatus.NotGradable, null, null, null, null, null, null, "none", reason);

    /// <summary>Whether the trade was decided with the information of the ticket: on F, or at the close of D on a daily-or-longer trigger.</summary>
    private static bool Belongs(SimTrade t, DateOnly forDate, DateOnly informationDate)
    {
        var decided = SessionClock.DateOf(t.DecisionEpochSeconds - 60);
        return decided == forDate || (decided == informationDate && t.TriggerTimeframe >= Timeframe.D1);
    }

    private static GraderResult Pending(string reason) => new(null, reason);

    private static GraderResult Done(GradeStatus status, string basis, string detail) => new(new TicketGrade(status, null, null, null, null, null, null, basis, detail), null);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

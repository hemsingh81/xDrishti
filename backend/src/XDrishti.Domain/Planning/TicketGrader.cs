using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Turns the simulator's record of one instrument over the planned session F into the grade of a ticket (P7-13, ADR 0090). Pure: values in, values out.
/// <list type="bullet">
/// <item>Only a trade entered on F in the ticket's side counts (the first by entry instant); a trade of any other date is never graded, so nothing at or before the close of D can become a grade.</item>
/// <item>R is the net R of the charge sheet when the trade was charged, otherwise the R from the filled prices (the convention of the cells).</item>
/// <item>No bar of F yet, or a position still open at the end of the data, is <i>pending</i>, not a grade.</item>
/// <item>Anything the replay refused is "not gradable" with the reason; a session with no trade is "not triggered".</item>
/// </list>
/// </summary>
public static class TicketGrader
{
    /// <summary>The grade of a ticket for <paramref name="side"/> on <paramref name="forDate"/> from a run over exactly that date for one instrument.</summary>
    public static GraderResult Grade(SimulationRun run, Side side, DateOnly forDate)
    {
        ArgumentNullException.ThrowIfNull(run);
        var gross = "gross, no charges";
        var basis = run.Costs is { } costs ? "net of the charge sheet " + costs.SheetId : gross;
        if (run.Instruments.Count != 1)
        {
            return Done(GradeStatus.NotGradable, basis, "the replay did not cover exactly one instrument");
        }

        var inst = run.Instruments[0];
        if (inst.Skipped is SkipReason.NoBars || inst.LastBar is null || inst.LastBar < forDate)
        {
            return new GraderResult(null, "the stored bars do not yet reach " + Date(forDate));
        }

        if (inst.Skipped is { } skip)
        {
            return Done(GradeStatus.NotGradable, basis, "the replay skipped the instrument: " + skip.ToCode() + (inst.SkipDetail is { } d ? " (" + d + ")" : string.Empty));
        }

        var mine = run.Trades.Where(t => t.Trade.Side == side && t.Trade.TradeDate == forDate).OrderBy(t => t.Trade.EntryEpochSeconds).ToList();
        if (mine.Count > 0)
        {
            var t = mine[0].Trade;
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            var r = costed ? t.Costing!.RNet : t.RFilled;
            var detail = string.Create(CultureInfo.InvariantCulture, $"entered {Date(t.TradeDate)}, left {Date(SessionClock.DateOf(t.ExitEpochSeconds))} ({t.ExitReason})");
            return new GraderResult(new TicketGrade(GradeStatus.Graded, r, t.MaeR, t.MfeR, t.ExitReason.ToString(), t.EntryPrice, t.ExitPrice, costed ? basis : gross, detail), null);
        }

        if (run.OpenAtEnd.Any(t => t.Trade.Side == side && t.Trade.TradeDate == forDate))
        {
            return new GraderResult(null, "the position is still open at the end of the stored bars");
        }

        var f = run.Totals.Fates;
        if (f.SkippedQuantity + f.SkippedCost + f.ExcludedAcrossDiscontinuity + f.EntriesSuppressed > 0)
        {
            return Done(GradeStatus.NotGradable, basis, "the replay dropped the trade (sizing or cost rule, a price discontinuity or a flagged day)");
        }

        if (f.OrderPendingAtEnd > 0)
        {
            return new GraderResult(null, "the entry order was still pending at the end of the stored bars");
        }

        var why = f.LimitNotReached > 0 ? "the entry limit was not reached"
            : f.NoFillBar > 0 ? "no bar to fill the entry"
            : f.EntryThroughStop > 0 ? "the price opened through the stop"
            : f.AfterSquareOff > 0 ? "the signal came after the square-off time"
            : "the entry condition did not hold on " + Date(forDate);
        return Done(GradeStatus.NotTriggered, basis, why);
    }

    /// <summary>A grade for a ticket that cannot be replayed.</summary>
    public static TicketGrade NotGradable(string reason) => new(GradeStatus.NotGradable, null, null, null, null, null, null, "none", reason);

    private static GraderResult Done(GradeStatus status, string basis, string detail) => new(new TicketGrade(status, null, null, null, null, null, null, basis, detail), null);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

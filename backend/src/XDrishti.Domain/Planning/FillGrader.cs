using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Turns the fills linked to one ticket into its fill grade (P7-14, ADR 0093). Pure. Only a round trip that is complete (the exit quantity reaches the entry quantity) is graded; an open one is left for a later run (null).
/// Plan levels come from the plan-price grade: its simulated entry and initial stop; the risk per share is their distance. Nothing after the last exit fill is read.
/// </summary>
public static class FillGrader
{
    /// <summary>The grade, or null while the position is still open (or there is no entry).</summary>
    /// <param name="side">The ticket's side.</param>
    /// <param name="entries">The entry links.</param>
    /// <param name="exits">The exit links.</param>
    /// <param name="plan">The plan-price grade of the ticket, or null when there is none yet.</param>
    public static FillGrade? Grade(Side side, IReadOnlyList<JournalLink> entries, IReadOnlyList<JournalLink> exits, TicketGrade? plan)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(exits);
        var qty = entries.Sum(l => l.Qty);
        if (qty <= 0 || exits.Sum(l => l.Qty) < qty)
        {
            return null;
        }

        var entry = Average(entries, qty);
        var exit = Average(exits, qty);
        var direction = side == Side.Buy ? 1m : -1m;
        var gross = Math.Round(direction * (exit - entry) * qty, 2);
        var charges = Math.Round(Charges(entries, entries.Sum(l => l.Qty)) + Charges(exits, qty), 2);
        double? planEntry = plan?.EntryPrice;
        double? planStop = plan?.StopPrice;
        var risk = planEntry is { } pe && planStop is { } ps ? Math.Abs(pe - ps) : 0d;
        if (plan?.Status != GradeStatus.Graded || risk <= 0)
        {
            var why = plan is null ? "no plan grade" : plan.Status != GradeStatus.Graded ? "the plan grade is " + plan.Status.Code() : "the plan grade has no entry and stop";
            return new FillGrade(FillGrade.NoPlanLevels, qty, (double)entry, (double)exit, planEntry, planStop, plan?.R, null, null, gross, charges, null, Describe(qty, entry, exit, "not measured in R: " + why));
        }

        var slippage = (double)direction * ((double)entry - planEntry!.Value);
        var r = (double)(gross - charges) / (risk * qty);
        return new FillGrade(FillGrade.Graded, qty, (double)entry, (double)exit, planEntry, planStop, plan.R, slippage, slippage / risk, gross, charges, r, Describe(qty, entry, exit, "R is net of the recorded charges, against the plan's risk per share"));
    }

    private static decimal Average(IReadOnlyList<JournalLink> links, long qty)
    {
        var taken = 0L;
        var value = 0m;
        foreach (var l in links)
        {
            var q = Math.Min(l.Qty, qty - taken);
            value += l.Fill.Price * q;
            taken += q;
        }

        return value / qty;
    }

    private static decimal Charges(IReadOnlyList<JournalLink> links, long qty)
    {
        var taken = 0L;
        var sum = 0m;
        foreach (var l in links)
        {
            var q = Math.Min(l.Qty, qty - taken);
            sum += l.Fill.Qty == 0 ? 0m : l.Fill.Charges * q / l.Fill.Qty;
            taken += q;
        }

        return sum;
    }

    private static string Describe(long qty, decimal entry, decimal exit, string tail) =>
        string.Create(CultureInfo.InvariantCulture, $"{qty} shares in at {entry:0.####}, out at {exit:0.####}; {tail}");
}

using System.Globalization;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>One row of the paper execution waterfall.</summary>
/// <param name="Id">plan, slippage, charges, net, missed_triggers or real_slippage.</param>
/// <param name="Label">The words.</param>
/// <param name="Value">Mean R of the row.</param>
/// <param name="N">Trades in the mean.</param>
public sealed record ExecutionRow(string Id, string Label, Metric Value, int N);

/// <summary>
/// The execution gap of paper (spec P10-03, criterion 7): plan R, then modelled slippage, then charges, then net, as means over closed trades (one per ticket). Missed triggers and real slippage are not evaluable: paper has no real fills.
/// Signed as costs: a row below zero is R lost.
/// </summary>
public static class ExecutionGap
{
    /// <summary>The reason of the two rows paper cannot have.</summary>
    public const string NoRealFills = "paper has no real fills (P11)";

    /// <summary>Builds the rows from the exits of the counted tickets.</summary>
    public static IReadOnlyList<ExecutionRow> Rows(IReadOnlyList<ReviewExit> exits)
    {
        ArgumentNullException.ThrowIfNull(exits);
        var charged = exits.Where(e => e.RNet is not null).ToList();
        var all = exits.Count;
        var rows = new List<ExecutionRow>
        {
            new("plan", "Plan R (exact prices)", Mean(exits.Select(e => e.RExact).ToList(), "no closed trade"), all),
            new("slippage", "Modelled slippage", Mean(exits.Select(e => e.RGross - e.RExact).ToList(), "no closed trade"), all),
            new("charges", "Charges", Charged(charged, all, charged.Select(e => e.RNet!.Value - e.RGross).ToList()), charged.Count),
            new("net", "Net R", Charged(charged, all, charged.Select(e => e.RNet!.Value).ToList()), charged.Count),
            new("missed_triggers", "Missed triggers", Metric.NotEvaluable(NoRealFills), 0),
            new("real_slippage", "Real slippage", Metric.NotEvaluable(NoRealFills), 0),
        };
        return rows;
    }

    private static Metric Mean(IReadOnlyList<double> values, string emptyReason) =>
        values.Count == 0 ? Metric.Empty(emptyReason) : Metric.Of(values.Average());

    private static Metric Charged(List<ReviewExit> charged, int all, IReadOnlyList<double> values)
    {
        if (all == 0)
        {
            return Metric.Empty("no closed trade");
        }

        if (charged.Count == 0)
        {
            return Metric.Unavailable("no charge sheet is stored on any closed trade");
        }

        var mean = Mean(values, "no closed trade");
        return charged.Count == all ? mean : mean.AsProvisional(string.Create(CultureInfo.InvariantCulture, $"only {charged.Count} of {all} closed trades carry a stored sheet's charges"));
    }
}

using System.Globalization;
using System.Reflection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// What the comparison reads of one costing, in one vocabulary for both sides: the fate (kept, one of the two skips, or the error of a date before the first schedule), the quantity, the risk taken, the two turnovers, the sixteen lines
/// of the two legs with the leg totals, the total, <c>CostR</c> and <c>RNet</c> (the two doubles), and the two echoes (the segment and the sheet id). After a skip only the fate is read: the engine keeps nothing of a skipped trade.
/// </summary>
internal sealed record CostView(
    string Fate,
    long Quantity,
    decimal RiskTaken,
    decimal EntryTurnover,
    decimal ExitTurnover,
    decimal EntryBrokerage,
    decimal EntryStt,
    decimal EntryExchange,
    decimal EntrySebi,
    decimal EntryStamp,
    decimal EntryOther,
    decimal EntryDp,
    decimal EntryGst,
    decimal EntryTotal,
    decimal ExitBrokerage,
    decimal ExitStt,
    decimal ExitExchange,
    decimal ExitSebi,
    decimal ExitStamp,
    decimal ExitOther,
    decimal ExitDp,
    decimal ExitGst,
    decimal ExitTotal,
    decimal TotalCost,
    double CostR,
    double RNet,
    string Segment,
    string SheetId)
{
    internal const string Kept = "kept";

    internal const string SkipQuantity = "skip:quantity";

    internal const string SkipCost = "skip:cost";

    internal static CostView OfFate(string fate) => new(fate, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0.0, 0.0, string.Empty, string.Empty);

    /// <summary>The oracle's costing as a view. <paramref name="sheetId"/> is the id the engine's sheet has (the adapter lower-cases the oracle's).</summary>
    internal static CostView Of(OracleCostResult r, OracleCostSegment segment, string sheetId)
    {
        if (r.Skip == OracleCostSkip.Quantity)
        {
            return OfFate(SkipQuantity);
        }

        if (r.Skip == OracleCostSkip.Cost)
        {
            return OfFate(SkipCost);
        }

        var e = r.EntryLeg!;
        var x = r.ExitLeg!;
        return new CostView(
            Kept, r.Quantity, r.RiskTaken, r.EntryTurnover, r.ExitTurnover,
            e.Brokerage, e.Stt, e.Exchange, e.Sebi, e.Stamp, e.Other, e.Dp, e.Gst, e.Total,
            x.Brokerage, x.Stt, x.Exchange, x.Sebi, x.Stamp, x.Other, x.Dp, x.Gst, x.Total,
            r.TotalCost, r.CostRDouble, r.RNet!.Value, segment.ToString(), sheetId);
    }

    /// <summary>The engine's answer as a view.</summary>
    internal static CostView Of(Result<CostApplication> applied)
    {
        if (applied.IsFailure)
        {
            return OfFate("error:" + applied.Error.Code);
        }

        if (applied.Value.Skip is { } skip)
        {
            return OfFate(skip == TradeSkip.QuantityBelowOne ? SkipQuantity : SkipCost);
        }

        var c = applied.Value.Trade!.Costing!;
        var e = c.EntryLeg;
        var x = c.ExitLeg;
        return new CostView(
            Kept, c.Quantity, c.RiskTaken, c.EntryTurnover, c.ExitTurnover,
            e.Brokerage, e.Stt, e.ExchangeTxn, e.SebiFee, e.StampDuty, e.OtherCharges, e.DpCharge, e.Gst, e.Total,
            x.Brokerage, x.Stt, x.ExchangeTxn, x.SebiFee, x.StampDuty, x.OtherCharges, x.DpCharge, x.Gst, x.Total,
            c.TotalCost, c.CostR, c.RNet, SegmentName(c.Segment), c.SheetId);
    }

    private static string SegmentName(CostSegment s) => CostAdapter.Segment(s).ToString();
}

/// <summary>
/// The comparator of the costing (spec P3-08c, D5): money, quantity, fate and echoes exactly (a decimal by value, a double by its bits when it is not one of the two ratio fields); the two ratio fields <c>CostR</c> and <c>RNet</c> exactly first and then within the tolerance of
/// <see cref="RatioStats.Tolerance"/>, with the largest difference recorded. It reads the properties by reflection, so a field added to the view is compared without a line of code here.
/// </summary>
internal static class CostComparer
{
    private static readonly PropertyInfo[] Properties = typeof(CostView).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToArray();

    /// <summary>The names of the compared fields.</summary>
    internal static IReadOnlyList<string> Fields { get; } = [.. Properties.Select(p => p.Name)];

    /// <summary>The two fields that are ratios (doubles): exact first, then the tolerance of D5.</summary>
    internal static readonly IReadOnlySet<string> RatioFields = new HashSet<string>(StringComparer.Ordinal) { nameof(CostView.CostR), nameof(CostView.RNet) };

    internal static List<Mismatch> Diff(CostView oracle, CostView engine, RatioStats stats, string where)
    {
        var found = new List<Mismatch>();
        foreach (var property in Properties)
        {
            var a = property.GetValue(oracle);
            var b = property.GetValue(engine);
            if (RatioFields.Contains(property.Name))
            {
                if (!stats.Accept(property.Name, (double)a!, (double)b!, where))
                {
                    found.Add(new Mismatch(property.Name, Show(a), Show(b)));
                }
            }
            else if (!Same(a, b))
            {
                found.Add(new Mismatch(property.Name, Show(a), Show(b)));
            }
        }

        return found;
    }

    private static bool Same(object? a, object? b) => (a, b) switch
    {
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        _ => Equals(a, b),
    };

    internal static string Show(object? value) => value switch
    {
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

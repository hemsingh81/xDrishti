using System.Globalization;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// The bill check of a charge sheet (spec P3-03f, F5): every row of the original global transaction report is classified without guessing, charged by <see cref="CostModel"/> and compared line by line with the seven cells of the bill. Pure; it keeps
/// no bill number, scrip name, client code, quantity or value in its answer. A row that fits no class is listed (date and reason) and not compared; a row with a negative Oth. cell is an adjustment line and is left out of the pass count.
/// </summary>
internal static class CostBillCheck
{
    /// <summary>The roadmap's tolerance per bill, in rupees.</summary>
    public const decimal Tolerance = 0.05m;

    /// <summary>The most rows listed one by one as unclassified.</summary>
    public const int MaxUnclassifiedListed = 20;

    /// <summary>The most rows in the list of the largest differences.</summary>
    public const int MaxLargest = 10;

    public const string IntradayName = "intraday equity";

    public const string DeliveryName = "delivery equity";

    private const decimal Crore = 10_000_000m;

    private static readonly string[] LineNames = ["brokerage", "gst", "stt", "sebi", "stamp duty", "txn charges", "other"];

    /// <summary>Compares the rows with the model. <paramref name="refused"/> and <paramref name="repeated"/> are counts the caller got from the reader; <paramref name="today"/> dates the suggested note.</summary>
    public static CostCheckReport Run(CostSheet sheet, IReadOnlyList<GtrRow> rows, int files, int refused, int repeated, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rows);

        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unclassified = new List<CostCheckUnclassified>();
        var unclassifiedTotal = 0;
        var adjustments = 0;
        var adjustmentSum = 0m;
        var unmodelledOther = 0;
        var compared = new List<Compared>();

        foreach (var row in rows)
        {
            var skip = BillTrades.NotEquity(row, BillName.Parse(row.Name)) ?? (BillTrades.IsBse(row) ? TradeSkips.Bse : null);
            if (skip is not null)
            {
                skipped[skip] = skipped.GetValueOrDefault(skip) + 1;
                continue;
            }

            var outcome = Classify(sheet, row);
            if (outcome.Problem is { } problem)
            {
                unclassifiedTotal++;
                if (unclassified.Count < MaxUnclassifiedListed)
                {
                    unclassified.Add(new CostCheckUnclassified(row.Date, problem));
                }

                continue;
            }

            var lines = row.Lines!;
            if (lines.OtherCharges < 0m)
            {
                adjustments++;
                adjustmentSum += lines.OtherCharges;
                continue;
            }

            var rates = outcome.Segment == CostSegment.Delivery ? outcome.Schedule!.Delivery : outcome.Schedule!.IntradayEquity;
            if (rates.OtherPct == 0m && lines.OtherCharges > 0m)
            {
                unmodelledOther++;
            }

            compared.Add(Compare(sheet, row, outcome.Segment, rates.OtherPct > 0m));
        }

        var classes = new List<CostCheckClass>();
        foreach (var (segment, name) in new[] { (CostSegment.IntradayEquity, IntradayName), (CostSegment.Delivery, DeliveryName) })
        {
            var members = compared.Where(c => c.Segment == segment).ToList();
            if (members.Count > 0)
            {
                classes.Add(Summarise(name, members));
            }
        }

        var largest = compared
            .Where(c => c.Total != 0m)
            .OrderByDescending(c => Math.Abs(c.Total)).ThenBy(c => c.Row.Date).ThenBy(c => c.Segment)
            .Take(MaxLargest)
            .Select(c => new CostCheckRow(c.Row.Date, c.Segment == CostSegment.Delivery ? DeliveryName : IntradayName, c.Total))
            .ToList();

        return new CostCheckReport(
            sheet.Id,
            sheet.RatesSha256,
            files,
            rows.Count,
            refused,
            repeated,
            classes,
            [.. skipped.Select(s => new CostCheckCount(s.Key, s.Value))],
            unclassified,
            unclassifiedTotal,
            adjustments,
            adjustmentSum,
            unmodelledOther,
            largest,
            Suggest(classes, today));
    }

    private static string? Suggest(List<CostCheckClass> classes, DateOnly today)
    {
        if (classes.Count == 0 || classes.Any(c => c.Beyond > 0))
        {
            return null;
        }

        var parts = classes.Select(c => c.Rows.ToString(CultureInfo.InvariantCulture) + (c.Name == DeliveryName ? " delivery" : " intraday") + " rows within " + Tolerance.ToString("0.00", CultureInfo.InvariantCulture));
        return "check " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + string.Join(", ", parts);
    }

    /// <summary>The class of a row, or the reason it has none. Never a guess: a one-sided row is delivery only when its brokerage cell is 0 and the sheet's delivery brokerage is 0.</summary>
    private static Classified Classify(CostSheet sheet, GtrRow row)
    {
        var schedule = sheet.RatesOn(row.Date);
        if (schedule.IsFailure)
        {
            return Classified.Not("the sheet has no rates for this date");
        }

        if (row.Lines is null)
        {
            return Classified.Not("the charge cells are not available");
        }

        if (row.BuyQty > 0 && row.SellQty > 0)
        {
            return new Classified(CostSegment.IntradayEquity, schedule.Value, null);
        }

        if (ReportCells.Norm(row.Exchange) != "nse")
        {
            return Classified.Not("the exchange is not NSE");
        }

        var delivery = schedule.Value.Delivery;
        var zeroBrokerage = delivery.BrokerageCap == 0m || (delivery.BrokeragePct == 0m && delivery.BrokerageMin == 0m);
        if (!zeroBrokerage)
        {
            return Classified.Not("a one-sided row cannot be told from a delivery row while the sheet charges delivery brokerage");
        }

        return row.Lines.Brokerage == 0m
            ? new Classified(CostSegment.Delivery, schedule.Value, null)
            : Classified.Not("a one-sided row with brokerage, while the sheet's delivery brokerage is 0");
    }

    private static Compared Compare(CostSheet sheet, GtrRow row, CostSegment segment, bool otherModelled)
    {
        var legs = new List<(Side Side, decimal Value)>(2);
        if (row.BuyQty > 0)
        {
            legs.Add((Side.Buy, row.BuyValue));
        }

        if (row.SellQty > 0)
        {
            legs.Add((Side.Sell, row.SellValue));
        }

        var brokerage = 0m;
        var gst = 0m;
        var stt = 0m;
        var sebi = 0m;
        var stamp = 0m;
        var txn = 0m;
        var other = 0m;
        foreach (var (side, value) in legs)
        {
            var leg = CostModel.Leg(sheet, segment, side, value, row.Date).Value;
            brokerage += leg.Brokerage;
            gst += leg.Gst;
            stt += leg.Stt;
            sebi += leg.SebiFee;
            stamp += leg.StampDuty;
            txn += leg.ExchangeTxn;
            other += leg.OtherCharges;
        }

        var cells = row.Lines!;
        decimal[] modelled = [brokerage, gst, stt, sebi, stamp, txn, other];
        decimal[] billed = [cells.Brokerage, cells.Gst, cells.Stt, cells.SebiFees, cells.StampDuty, cells.TxnCharges, cells.OtherCharges];
        var differences = new decimal[LineNames.Length];
        var total = 0m;
        for (var i = 0; i < differences.Length; i++)
        {
            differences[i] = modelled[i] - billed[i];
            if (i < 6 || otherModelled)
            {
                total += differences[i];
            }
        }

        return new Compared(row, segment, differences, total, billed);
    }

    private static CostCheckClass Summarise(string name, List<Compared> members)
    {
        var passed = members.Count(m => Math.Abs(m.Total) <= Tolerance);
        var lines = new List<CostCheckLine>();
        for (var i = 0; i < LineNames.Length; i++)
        {
            var largest = members.Max(m => Math.Abs(m.Differences[i]));
            var implied = Implied(members, i);
            lines.Add(new CostCheckLine(LineNames[i], largest, implied));
        }

        return new CostCheckClass(name, members.Count, passed, members.Count - passed, members.Max(m => Math.Abs(m.Total)), lines);
    }

    /// <summary>
    /// The rate a line implies over the rows of a class where the bill's cell is above 0 (a proposal, never stored): a percent of the turnover of the row (buy plus sell), except STT (a percent of the value it is charged on: the sell value of
    /// an intraday row, the single side of a delivery row), stamp duty (a percent of the buy value), the SEBI fee (rupees per crore of turnover) and GST (a percent of the brokerage, SEBI and transaction cells of the row). Rounded to six decimals.
    /// </summary>
    private static CostCheckImplied? Implied(List<Compared> members, int line)
    {
        var rates = new List<decimal>();
        foreach (var member in members)
        {
            var cell = member.Billed[line];
            if (cell <= 0m)
            {
                continue;
            }

            var row = member.Row;
            var turnover = row.BuyValue + row.SellValue;
            decimal basis;
            decimal scale;
            switch (line)
            {
                case 1:
                    basis = member.Billed[0] + member.Billed[3] + member.Billed[5];
                    scale = 100m;
                    break;
                case 2:
                    basis = member.Segment == CostSegment.IntradayEquity ? row.SellValue : turnover;
                    scale = 100m;
                    break;
                case 3:
                    basis = turnover;
                    scale = Crore;
                    break;
                case 4:
                    basis = row.BuyValue;
                    scale = 100m;
                    break;
                default:
                    basis = turnover;
                    scale = 100m;
                    break;
            }

            if (basis > 0m)
            {
                rates.Add(Math.Round(cell * scale / basis, 6, MidpointRounding.AwayFromZero));
            }
        }

        if (rates.Count == 0)
        {
            return null;
        }

        rates.Sort();
        var middle = rates.Count / 2;
        var median = rates.Count % 2 == 1 ? rates[middle] : Math.Round((rates[middle - 1] + rates[middle]) / 2m, 6, MidpointRounding.AwayFromZero);
        return new CostCheckImplied(rates[0], median, rates[^1], rates.Count);
    }

    private sealed record Classified(CostSegment Segment, ChargeSchedule? Schedule, string? Problem)
    {
        public static Classified Not(string problem) => new(CostSegment.IntradayEquity, null, problem);
    }

    private sealed record Compared(GtrRow Row, CostSegment Segment, decimal[] Differences, decimal Total, decimal[] Billed);
}

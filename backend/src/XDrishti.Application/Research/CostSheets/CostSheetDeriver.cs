using System.Globalization;
using System.Text;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// The step from the bills to a proposed sheet (spec CS-01). Pure. Rows are classified without guessing (a round trip is intraday; a one-sided NSE row with brokerage 0 is delivery; anything else is not used), split by date into the older share the rates
/// come from and the newest share they are tested on, and the rates are the medians of <see cref="CostBillCheck.Implied"/> (the algorithm <c>check</c> prints, not a second one). A line no derive bill has above 0 is null in the text and
/// <c>unavailable</c> in the printout, never a zero and never a default. The residuals price every bill with the proposed sheet, a null line counting as the 0 the bills show.
/// </summary>
/// <remarks>
/// The rule: a derived sheet never understates a cost. What the bills prove is written; what they cannot prove is null (<c>unavailable</c>, "fill from your tariff") so the sheet cannot be loaded until the owner fills it. Proven: the brokerage rate (from the bills
/// below the per-leg cap only, null when it moves by more than 5 %), the per-leg brokerage cap (only when bills of different turnover show the same largest brokerage, all bigger than every other bill), the other rates. Null: the cap without that proof, the delivery DP
/// charge, the delivery brokerage when a one-sided NSE bill has brokerage. Assumed and printed one by one: the brokerage minimum 0, the intraday buy-side STT 0, the delivery STT on both sides, the intraday DP charge 0 and the rounding rule.
/// </remarks>
internal static class CostSheetDeriver
{
    public const decimal MinSplit = 0.5m;

    public const decimal MaxSplit = 0.9m;

    /// <summary>A bill is reproduced when the modelled total is within this many rupees of the billed total.</summary>
    public const decimal WithinRupees = 1m;

    /// <summary>The done-when rule: the share of bills reproduced.</summary>
    public const decimal RequiredShare = 0.95m;

    /// <summary>A line whose implied rate has (max - min) / median above this over the derive bills is unstable.</summary>
    public const decimal UnstableRatio = 0.05m;

    public const string DeriveSet = "derive";

    public const string CheckSet = "check";

    public const string AllClasses = "all";

    private const string DpLine = "dp charge";

    /// <summary>The largest brokerage a sheet may hold per leg (the domain's bound): the residuals price an unproven cap as this, that is, no cap.</summary>
    private const decimal PricingCap = 10_000m;

    // the ten field names of a class, in ChargeRates order
    private static readonly string[] Fields =
        ["brokerage_pct", "brokerage_cap", "brokerage_min", "stt_buy_pct", "stt_sell_pct", "exchange_txn_pct", "sebi_per_crore", "stamp_buy_pct", "other_pct", "dp_charge_per_sell"];

    public static Result<CostDerivation> Run(IReadOnlyList<GtrRow> rows, int files, int refused, int repeated, decimal split, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unused = 0;
        var oneSided = 0;
        var used = new List<CostBillCheck.Compared>();
        foreach (var row in rows)
        {
            var skip = BillTrades.NotEquity(row, BillName.Parse(row.Name)) ?? (BillTrades.IsBse(row) ? TradeSkips.Bse : null);
            if (skip is not null)
            {
                skipped[skip] = skipped.GetValueOrDefault(skip) + 1;
                continue;
            }

            var cells = row.Lines;
            var segment = cells is null ? null : SegmentOf(row, cells);
            if (cells is not null && segment is null && (row.BuyQty == 0 || row.SellQty == 0) && ReportCells.Norm(row.Exchange) == "nse" && cells.Brokerage > 0m)
            {
                // a one-sided NSE bill with brokerage: not a delivery bill as far as the sheet goes; counted on its own
                oneSided++;
                continue;
            }

            if (cells is null || segment is null || cells.OtherCharges < 0m)
            {
                unused++;
                continue;
            }

            decimal[] billed = [cells.Brokerage, cells.Gst, cells.Stt, cells.SebiFees, cells.StampDuty, cells.TxnCharges, cells.OtherCharges];
            used.Add(new CostBillCheck.Compared(row, segment.Value, [], 0m, billed));
        }

        if (used.Count == 0)
        {
            return Result.Failure<CostDerivation>(PortfolioErrors.Invalid("file", "No bill row of an NSE cash equity trade with its seven charge cells could be used."));
        }

        var ordered = used.OrderBy(c => c.Row.Date).ThenBy(c => c.Row.Line).ToList();
        var count = Math.Min(ordered.Count, (int)Math.Ceiling(ordered.Count * split));
        while (count < ordered.Count && ordered[count].Row.Date == ordered[count - 1].Row.Date)
        {
            count++;
        }

        var derive = ordered.Take(count).ToList();
        var from = derive[0].Row.Date;
        var to = derive[^1].Row.Date;
        var lines = new List<DerivedLine>();

        var assumed = new List<string> { "rounding half_away_from_zero (the report does not say how the broker rounds; each line of a bill is in whole paise)" };
        var gst = Line(lines, AllClasses, 1, derive, weighted: true);
        var intraday = ClassRates(lines, CostBillCheck.IntradayName, CostSegment.IntradayEquity, derive, oneSided, assumed);
        var delivery = ClassRates(lines, CostBillCheck.DeliveryName, CostSegment.Delivery, derive, oneSided, assumed);

        var id = "derived-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var source = FormattableString.Invariant($"derived from {derive.Count} bills, {from:yyyy-MM-dd}..{to:yyyy-MM-dd}");
        var json = Json(id, source, from, gst?.Median, intraday, delivery);

        var built = Working(id, source, from, gst?.Median, intraday, delivery);
        if (built.IsFailure)
        {
            return Result.Failure<CostDerivation>(built.Error);
        }

        var (residuals, deriveSet, checkSet) = Residuals(built.Value, ordered, count);
        var csv = new StringBuilder("date,class,set,billed_total,modelled_total,difference\n");
        foreach (var r in residuals)
        {
            csv.Append(FormattableString.Invariant($"{r.Date:yyyy-MM-dd},{r.Class},{r.Set},{r.Billed:0.00},{r.Modelled:0.00},{r.Difference:0.00}\n"));
        }

        var largest = residuals
            .Where(r => r.Difference != 0m)
            .OrderByDescending(r => Math.Abs(r.Difference)).ThenBy(r => r.Date).ThenBy(r => r.Class, StringComparer.Ordinal)
            .Take(CostBillCheck.MaxLargest)
            .ToList();

        return Result.Success(new CostDerivation(
            id,
            json,
            csv.ToString(),
            files,
            rows.Count,
            refused,
            repeated,
            [.. skipped.Select(s => new CostCheckCount(s.Key, s.Value))],
            unused,
            oneSided,
            assumed,
            from,
            to,
            lines,
            deriveSet,
            checkSet,
            largest));
    }

    /// <summary>The class of a row, or null when it fits none (never a guess).</summary>
    private static CostSegment? SegmentOf(GtrRow row, GtrLines cells)
    {
        if (row.BuyQty > 0 && row.SellQty > 0)
        {
            return CostSegment.IntradayEquity;
        }

        return ReportCells.Norm(row.Exchange) == "nse" && cells.Brokerage == 0m ? CostSegment.Delivery : null;
    }

    /// <summary>
    /// The implied rate of one line over the members, added to the lines with its instability (and, when unstable, the dates the rate moved: those of the bills whose rate is farthest from the median). With <paramref name="nullIfUnstable"/> an unstable rate is
    /// written as unavailable and null is returned. With <paramref name="weighted"/> the written rate is the sum of the line over the sum of its bases, not the median of the per-bill rates (GST).
    /// </summary>
    private static CostCheckImplied? Line(List<DerivedLine> lines, string cls, int index, List<CostBillCheck.Compared> members, bool nullIfUnstable = false, bool weighted = false)
    {
        var rates = CostBillCheck.ImpliedRates(members, index);
        var implied = CostBillCheck.Implied(members, index);
        var unstable = implied is not null && implied.Median > 0m && (implied.Maximum - implied.Minimum) / implied.Median > UnstableRatio;
        DateOnly? first = null;
        DateOnly? last = null;
        if (unstable)
        {
            var threshold = implied!.Median * UnstableRatio / 2m;
            var moved = rates.Where(r => Math.Abs(r.Rate - implied.Median) > threshold).Select(r => r.Member.Row.Date).ToList();
            first = moved.Min();
            last = moved.Max();
        }

        if (weighted && implied is not null)
        {
            var sum = rates.Sum(r => r.Member.Billed[index]);
            var bases = rates.Sum(r => r.Member.Billed[0] + r.Member.Billed[3] + r.Member.Billed[5]);
            implied = implied with { Median = Math.Round(sum * 100m / bases, 6, MidpointRounding.AwayFromZero) };
        }

        if (unstable && nullIfUnstable)
        {
            lines.Add(new DerivedLine(cls, CostBillCheck.LineNames[index], null, implied, true, first, last, "the rate moves by more than 5 % of its median, so no single rate is proven"));
            return null;
        }

        lines.Add(new DerivedLine(cls, CostBillCheck.LineNames[index], implied?.Median, implied, unstable, first, last));
        return implied;
    }

    /// <summary>
    /// The per-leg brokerage cap the bills prove: the largest billed brokerage of an intraday bill (two legs, so half of it per leg) when bills of at least two different turnovers show exactly that amount, there is a bill below it, and every bill at the amount has
    /// a larger turnover than every bill below it. Anything else (one turnover, or the largest at the smallest turnovers, which a minimum also gives) is no proof: null.
    /// </summary>
    private static (decimal? Cap, decimal Largest, int Bills) CapProof(List<CostBillCheck.Compared> members)
    {
        var largest = members.Max(m => m.Billed[0]);
        if (largest <= 0m)
        {
            return (null, 0m, 0);
        }

        var atLargest = members.Where(m => m.Billed[0] == largest).ToList();
        var below = members.Where(m => m.Billed[0] < largest).ToList();
        static decimal Turnover(CostBillCheck.Compared m) => m.Row.BuyValue + m.Row.SellValue;
        var proven = below.Count > 0
            && atLargest.Select(Turnover).Distinct().Count() >= 2
            && atLargest.Min(Turnover) > below.Max(Turnover);
        return proven ? (largest / 2m, largest, atLargest.Count) : (null, largest, atLargest.Count);
    }

    /// <summary>The ten values of a class (null where unavailable); all null when the class has no derive bill.</summary>
    private static decimal?[] ClassRates(List<DerivedLine> lines, string cls, CostSegment segment, List<CostBillCheck.Compared> derive, int oneSided, List<string> assumed)
    {
        var members = derive.Where(m => m.Segment == segment).ToList();
        var values = new decimal?[Fields.Length];
        if (members.Count == 0)
        {
            return values;
        }

        var delivery = segment == CostSegment.Delivery;
        if (delivery)
        {
            if (oneSided > 0)
            {
                lines.Add(new DerivedLine(cls, CostBillCheck.LineNames[0], null, null, false, null, null, N(oneSided) + " one-sided NSE bills have brokerage above 0, so delivery brokerage is not zero for certain"));
            }
            else
            {
                // every one-sided NSE bill has brokerage 0: that is what makes it a delivery bill
                values[0] = 0m;
                values[1] = 0m;
                values[2] = 0m;
                lines.Add(new DerivedLine(cls, CostBillCheck.LineNames[0], 0m, null, false, null, null));
            }
        }
        else
        {
            var (cap, largest, atLargest) = CapProof(members);
            var rated = cap is null ? members : members.Where(m => m.Billed[0] < largest).ToList();
            var brokerage = Line(lines, cls, 0, rated, nullIfUnstable: true);
            if (brokerage is not null)
            {
                values[0] = brokerage.Median;
                values[1] = cap;
                values[2] = 0m;
                assumed.Add(cls + " brokerage_min 0 (the bills cannot show a minimum per leg; a tariff with a minimum charges small trades more)");
            }

            lines.Add(new DerivedLine(cls, "brokerage cap", cap, null, false, null, null, cap is null
                ? "no proof of a cap (needs bills of different sizes at the same largest brokerage, all bigger than the others)"
                : N(atLargest) + " bills show the same largest brokerage; per leg it is half of it"));
        }

        var stt = Line(lines, cls, 2, members);
        var sebi = Line(lines, cls, 3, members);
        var stamp = Line(lines, cls, 4, members);
        var txn = Line(lines, cls, 5, members);
        var other = Line(lines, cls, 6, members);
        values[3] = delivery ? stt?.Median : (stt is null ? null : 0m);
        values[4] = stt?.Median;
        values[5] = txn?.Median;
        values[6] = sebi?.Median;
        values[7] = stamp?.Median;
        values[8] = other?.Median;
        if (stt is not null)
        {
            assumed.Add(delivery
                ? cls + " stt_buy_pct = stt_sell_pct (the bills show one rate; the two sides are not told apart)"
                : cls + " stt_buy_pct 0 (the implied rate is on the sell value only)");
        }

        if (delivery)
        {
            lines.Add(new DerivedLine(cls, DpLine, null, null, false, null, null, "not a column of the report (any DP charge is inside the other line, if billed at all)"));
        }
        else
        {
            values[9] = 0m;
            assumed.Add(cls + " dp_charge_per_sell 0 (no DP charge on an intraday bill)");
            lines.Add(new DerivedLine(cls, DpLine, 0m, null, false, null, null));
        }

        return values;
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(decimal? value) => value is null ? "null" : value.Value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Json(string id, string source, DateOnly from, decimal? gst, decimal?[] intraday, decimal?[] delivery)
    {
        var text = new StringBuilder();
        text.Append("{\n  \"id\": \"").Append(id).Append("\",\n  \"label\": \"derived from the owner's Dhan reports (unchecked)\",\n  \"source\": \"").Append(source).Append("\",\n  \"schedules\": [\n    {\n");
        text.Append("      \"effective_from\": \"").Append(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\",\n");
        text.Append("      \"gst_pct\": ").Append(Num(gst)).Append(",\n      \"rounding\": \"half_away_from_zero\",\n");
        Rates(text, "intraday_equity", intraday, last: false);
        Rates(text, "delivery_equity", delivery, last: true);
        text.Append("    }\n  ]\n}\n");
        return text.ToString();
    }

    private static void Rates(StringBuilder text, string name, decimal?[] values, bool last)
    {
        text.Append("      \"").Append(name).Append("\": {\n");
        for (var i = 0; i < Fields.Length; i++)
        {
            text.Append("        \"").Append(Fields[i]).Append("\": ").Append(Num(values[i])).Append(i < Fields.Length - 1 ? ",\n" : "\n");
        }

        text.Append(last ? "      }\n" : "      },\n");
    }

    /// <summary>The sheet the residuals price with: a null line counts as 0 (no derive bill has it above 0).</summary>
    private static Result<CostSheet> Working(string id, string source, DateOnly from, decimal? gst, decimal?[] intraday, decimal?[] delivery)
    {
        var a = Rates(Priced(intraday));
        var b = Rates(Priced(delivery));
        if (a.IsFailure || b.IsFailure)
        {
            return Result.Failure<CostSheet>((a.IsFailure ? a : b).Error);
        }

        var schedule = ChargeSchedule.Create(from, gst ?? 0m, ChargeRounding.HalfAwayFromZero, a.Value, b.Value);
        return schedule.IsFailure ? Result.Failure<CostSheet>(schedule.Error) : CostSheet.Create(id, "derived (unchecked)", source, null, [schedule.Value]);
    }

    /// <summary>For the residuals only: a brokerage rate without a proven cap is priced uncapped (the bills show no cap binding).</summary>
    private static decimal?[] Priced(decimal?[] values)
    {
        var priced = (decimal?[])values.Clone();
        if (priced[0] is > 0m && priced[1] is null)
        {
            priced[1] = PricingCap;
        }

        return priced;
    }

    private static Result<ChargeRates> Rates(decimal?[] v) =>
        ChargeRates.Create(v[0] ?? 0m, v[1] ?? 0m, v[2] ?? 0m, v[3] ?? 0m, v[4] ?? 0m, v[5] ?? 0m, v[6] ?? 0m, v[7] ?? 0m, v[8] ?? 0m, v[9] ?? 0m);

    private static (List<DerivedResidual> Residuals, DerivedSet Derive, DerivedSet Check) Residuals(CostSheet sheet, List<CostBillCheck.Compared> ordered, int deriveCount)
    {
        var residuals = new List<DerivedResidual>(ordered.Count);
        var within = new[] { 0, 0 };
        for (var i = 0; i < ordered.Count; i++)
        {
            var bill = ordered[i];
            var schedule = sheet.Schedules[0];
            var delivery = bill.Segment == CostSegment.Delivery;
            var otherModelled = (delivery ? schedule.Delivery : schedule.IntradayEquity).OtherPct > 0m;
            var compared = CostBillCheck.Compare(sheet, bill.Row, bill.Segment, otherModelled);
            var billed = bill.Billed.Take(6).Sum() + (otherModelled ? bill.Billed[6] : 0m);
            var set = i < deriveCount ? 0 : 1;
            if (Math.Abs(compared.Total) <= WithinRupees)
            {
                within[set]++;
            }

            residuals.Add(new DerivedResidual(bill.Row.Date, delivery ? CostBillCheck.DeliveryName : CostBillCheck.IntradayName, set == 0 ? DeriveSet : CheckSet, billed, billed + compared.Total, compared.Total));
        }

        return (residuals, Set(DeriveSet, deriveCount, within[0]), Set(CheckSet, ordered.Count - deriveCount, within[1]));
    }

    private static DerivedSet Set(string name, int bills, int within) => new(name, bills, within, bills == 0 ? 0m : (decimal)within / bills);
}

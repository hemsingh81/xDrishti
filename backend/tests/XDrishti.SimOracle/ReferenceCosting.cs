namespace XDrishti.SimOracle;

/// <summary>
/// The reference costing of ADR 0042 D3 to D13 and ADR 0047 R11 and R12, in decimal. Written from the rule text alone.
/// The faults argument is for tests; the trace (a set of rule ids) is what the coverage counts.
/// </summary>
public static class ReferenceCosting
{
    private const decimal Crore = 10_000_000m;

    /// <summary>One amount to the paisa, once, by the mode (a fault may change the mode or skip the rounding).</summary>
    public static decimal Round(decimal x, OracleRounding mode, OracleCostFaults faults = OracleCostFaults.None)
    {
        if (faults.HasFlag(OracleCostFaults.RoundingPerTrade))
        {
            return x;
        }

        if (faults.HasFlag(OracleCostFaults.HalfEvenInsteadOfHalfAway) && mode == OracleRounding.HalfAwayFromZero)
        {
            mode = OracleRounding.HalfEven;
        }

        return mode switch
        {
            OracleRounding.HalfAwayFromZero => Math.Round(x, 2, MidpointRounding.AwayFromZero),
            OracleRounding.HalfEven => Math.Round(x, 2, MidpointRounding.ToEven),
            _ => Math.Round(x, 2, MidpointRounding.ToZero),
        };
    }

    /// <summary>The quantity of D11: floor of the smaller of the risk bound and the capital bound, from the exact entry. May be 0.</summary>
    public static long Quantity(
        decimal capital,
        OracleSizing sizing,
        decimal entry,
        decimal stop,
        OracleCostFaults faults = OracleCostFaults.None)
    {
        decimal risk = Math.Abs(entry - stop);
        if (capital <= 0m || entry <= 0m || risk <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(capital), "capital, entry and risk must be above 0");
        }

        decimal byRisk = capital * sizing.RiskPct / 100m / risk;
        decimal byCap = capital * sizing.MaxCapitalPct / 100m / entry;
        decimal q = Math.Min(byRisk, byCap);
        return faults.HasFlag(OracleCostFaults.FloorReplacedByRound)
            ? (long)Math.Round(q, 0, MidpointRounding.AwayFromZero)
            : (long)Math.Floor(q);
    }

    /// <summary>The charge of one leg of turnover T (D8), each line rounded once by the schedule's mode.</summary>
    public static OracleLegLines Leg(
        decimal turnover,
        OracleSide side,
        OracleCostSegment segment,
        OracleSchedule schedule,
        OracleCostFaults faults = OracleCostFaults.None,
        ISet<string>? trace = null)
    {
        OracleRates r = schedule.RatesOf(segment);
        OracleRounding m = schedule.Rounding;
        bool buy = side == OracleSide.Buy;
        trace?.Add(segment == OracleCostSegment.Delivery ? CostTrace.Delivery : CostTrace.Intraday);
        trace?.Add(buy ? CostTrace.LegBuy : CostTrace.LegSell);
        trace?.Add(m switch
        {
            OracleRounding.HalfAwayFromZero => CostTrace.RoundAway,
            OracleRounding.HalfEven => CostTrace.RoundEven,
            _ => CostTrace.RoundDown,
        });

        decimal brokerage = 0m;
        if (r.BrokerageCap != 0m)
        {
            decimal pctAmount = turnover * r.BrokeragePct / 100m;
            decimal floored = Math.Max(r.BrokerageMin, pctAmount);
            brokerage = faults.HasFlag(OracleCostFaults.BrokerageCapIgnored) ? floored : Math.Min(r.BrokerageCap, floored);
            trace?.Add(pctAmount < r.BrokerageMin ? CostTrace.BrokerageMin
                : pctAmount > r.BrokerageCap ? CostTrace.BrokerageCap
                : CostTrace.BrokerageBetween);
        }

        decimal sttPct = buy && !faults.HasFlag(OracleCostFaults.SttOnBothSides) ? r.SttBuyPct : r.SttSellPct;

        decimal stampPct = buy || faults.HasFlag(OracleCostFaults.StampDutyOnSell) ? r.StampBuyPct : 0m;
        decimal dp = segment == OracleCostSegment.Delivery && (!buy || faults.HasFlag(OracleCostFaults.DpChargeOnBuy))
            ? r.DpChargePerSell
            : 0m;

        decimal rb = Round(brokerage, m, faults);
        decimal stt = Round(turnover * sttPct / 100m, m, faults);
        decimal exch = Round(turnover * r.ExchangePct / 100m, m, faults);
        decimal sebi = Round(turnover * r.SebiPerCrore / Crore, m, faults);
        decimal stamp = Round(turnover * stampPct / 100m, m, faults);
        decimal other = Round(turnover * r.OtherPct / 100m, m, faults);
        decimal gstBase = rb + exch + sebi + (faults.HasFlag(OracleCostFaults.GstOnWrongBase) ? stt : 0m);
        decimal gst = Round(schedule.GstPct / 100m * gstBase, m, faults);
        return new OracleLegLines(rb, stt, exch, sebi, stamp, other, dp, gst);
    }

    /// <summary>Sizes and charges a trade (D11, D13), the legs each on the schedule of their own date (D7).</summary>
    public static OracleCostResult Cost(
        OracleCostInput t,
        OracleChargeSheet sheet,
        OracleSizing sizing,
        OracleCostFaults faults = OracleCostFaults.None)
    {
        var trace = new HashSet<string>(StringComparer.Ordinal);
        decimal sizingEntry = faults.HasFlag(OracleCostFaults.QuantityFromFilledEntry) ? t.EntryFilled : t.EntryExact;
        long quantity = Quantity(t.Capital, sizing, sizingEntry, t.StopPrice, faults);
        decimal risk = Math.Abs(sizingEntry - t.StopPrice);
        decimal riskAmount = t.Capital * sizing.RiskPct / 100m;
        trace.Add(t.Side == OracleSide.Buy ? CostTrace.TradeBuy : CostTrace.TradeSell);
        if (quantity < 1)
        {
            trace.Add(CostTrace.QuantityZero);
            return new OracleCostResult(OracleCostSkip.Quantity, quantity, riskAmount, 0m, 0m, 0m, null, null, 0m, 0m, 0.0, null, trace);
        }

        decimal riskTaken = quantity * risk;
        decimal entryT = quantity * t.EntryFilled;
        decimal exitT = quantity * t.ExitFilled;
        OracleSchedule entrySchedule = sheet.In(t.EntryDate)
            ?? throw new InvalidOperationException("costs.no_rates_for_date: " + t.EntryDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        OracleSchedule exitSchedule = faults.HasFlag(OracleCostFaults.ExitLegUsesEntrySchedule)
            ? entrySchedule
            : sheet.In(t.ExitDate)
              ?? throw new InvalidOperationException("costs.no_rates_for_date: " + t.ExitDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (!ReferenceEquals(sheet.In(t.EntryDate), sheet.In(t.ExitDate)))
        {
            trace.Add(CostTrace.DateChange);
        }

        OracleSide exitSide = t.Side == OracleSide.Buy ? OracleSide.Sell : OracleSide.Buy;
        OracleLegLines entryLeg = Leg(entryT, t.Side, t.Segment, entrySchedule, faults, trace);
        OracleLegLines exitLeg = Leg(exitT, exitSide, t.Segment, exitSchedule, faults, trace);
        decimal total = entryLeg.Total + exitLeg.Total;
        if (faults.HasFlag(OracleCostFaults.RoundingPerTrade))
        {
            total = Round(total, OracleRounding.HalfAwayFromZero);
        }

        decimal divisor = faults.HasFlag(OracleCostFaults.CostRByBudget) ? riskAmount : riskTaken;
        decimal costR = total / divisor;
        double costRd = OracleNumbers.ToDouble(costR);
        bool skipCost = faults.HasFlag(OracleCostFaults.CostEqualToLimitSkipped) ? costR >= t.MaxCostR : costR > t.MaxCostR;
        if (skipCost)
        {
            trace.Add(CostTrace.CostSkip);
            return new OracleCostResult(OracleCostSkip.Cost, quantity, riskAmount, riskTaken, entryT, exitT, entryLeg, exitLeg, total, costR, costRd, null, trace);
        }

        double basis = faults.HasFlag(OracleCostFaults.NetRFromExactR) ? t.RExact : t.RFilled;
        double net = basis - costRd;
        return new OracleCostResult(OracleCostSkip.None, quantity, riskAmount, riskTaken, entryT, exitT, entryLeg, exitLeg, total, costR, costRd, net == 0.0 ? 0.0 : net, trace);
    }
}

namespace XDrishti.SimOracle;

/// <summary>
/// The seeded generator of cost cases (own SplitMix64). Every sheet it builds is SYNTHETIC: invented round numbers,
/// not the rates of any broker or exchange. The case is built to reach its category; the trace of the reference is what counts.
/// </summary>
public static class OracleCostSeries
{
    private static readonly decimal[] BrokeragePcts = [0.01m, 0.02m, 0.03m, 0.05m, 0.1m];
    private static readonly decimal[] SttPcts = [0m, 0.025m, 0.04m, 0.1m];
    private static readonly decimal[] ExchangePcts = [0.003m, 0.00345m, 0.005m];
    private static readonly decimal[] Stamps = [0m, 0.003m, 0.015m, 0.02m];
    private static readonly decimal[] Others = [0m, 0.001m];
    private static readonly decimal[] Dps = [0m, 10m, 13.5m, 15.34m];

    /// <summary>Builds the case of a category from a seed; the same arguments give the same case.</summary>
    public static OracleCostCase Of(OracleCostCategory category, long seed)
    {
        var rng = new SplitMix64(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)category + 1UL) * 0xD1B54A32D192ED03UL);
        OracleSide side = category switch
        {
            OracleCostCategory.SellTrade => OracleSide.Sell,
            OracleCostCategory.BuyTrade => OracleSide.Buy,
            _ => rng.Chance(50) ? OracleSide.Buy : OracleSide.Sell,
        };
        OracleCostSegment segment = category switch
        {
            OracleCostCategory.IntradaySegment => OracleCostSegment.IntradayEquity,
            OracleCostCategory.DeliverySegment or OracleCostCategory.DateChangeBetweenLegs => OracleCostSegment.Delivery,
            _ => rng.Chance(50) ? OracleCostSegment.Delivery : OracleCostSegment.IntradayEquity,
        };
        OracleRounding rounding = category switch
        {
            OracleCostCategory.RoundHalfAway => OracleRounding.HalfAwayFromZero,
            OracleCostCategory.RoundHalfEven => OracleRounding.HalfEven,
            OracleCostCategory.RoundDown => OracleRounding.Down,
            _ => (OracleRounding)rng.Range(0, 2),
        };
        int dir = side == OracleSide.Buy ? 1 : -1;
        bool zero = category == OracleCostCategory.QuantityZero;

        decimal entry = zero ? rng.Range(600_000, 3_000_000) / 100m : rng.Range(5_000, 200_000) / 100m;
        decimal risk = rng.Range(50, 800) / 100m;
        decimal stop = entry - dir * risk;
        decimal capital = zero ? rng.Range(2_000, 20_000) : rng.Range(200_000, 5_000_000);
        OracleSizing sizing = rng.Chance(50) ? OracleSizing.Standard : OracleSizing.Conservative;
        decimal slip = rng.Range(0, 3) * 0.05m;
        decimal entryFilled = entry + dir * slip;
        decimal exitFilled = Math.Max(1m, entry + dir * rng.Range(-150, 300) / 100m * risk);

        var a = new DateOnly(2026, 1, 1);
        DateOnly b = new DateOnly(2026, 3, 1).AddDays(rng.Range(0, 60));
        DateOnly entryDate;
        DateOnly exitDate;
        if (category == OracleCostCategory.DateChangeBetweenLegs)
        {
            entryDate = b.AddDays(-rng.Range(1, 20));
            exitDate = b.AddDays(rng.Range(0, 20));
        }
        else
        {
            entryDate = a.AddDays(rng.Range(0, 200));
            exitDate = segment == OracleCostSegment.Delivery ? entryDate.AddDays(rng.Range(0, 10)) : entryDate;
        }

        long q = zero ? 0 : ReferenceCosting.Quantity(capital, sizing, entry, stop);
        decimal tEntry = q * entryFilled;
        decimal tExit = q * exitFilled;
        OracleSchedule sa = Schedule(rng, a, rounding, category, tEntry, tExit);
        OracleSchedule sb = Schedule(rng, b, rounding, category, tEntry, tExit);
        var sheet = new OracleChargeSheet("synthetic-" + category, [sa, sb]);

        double rFilled = OracleNumbers.ToDouble(dir * (exitFilled - entryFilled) / risk);
        double rExact = OracleNumbers.ToDouble(dir * (exitFilled - entry) / risk);
        decimal maxCost = rng.Range(0, 2) switch
        {
            0 => 0.1m,
            1 => 0.2m,
            _ => 1000m,
        };
        var input = new OracleCostInput(side, entry, entryFilled, stop, exitFilled, rExact, rFilled, entryDate, exitDate, segment, capital, maxCost);
        if (category == OracleCostCategory.CostOverLimit && !zero)
        {
            OracleCostResult open = ReferenceCosting.Cost(input with { MaxCostR = 1_000_000m }, sheet, sizing);
            decimal over = seed % 4 == 0 ? open.CostR : open.CostR * 0.9m;
            input = input with { MaxCostR = over };
        }

        return new OracleCostCase(category, seed, sheet, sizing, input);
    }

    private static OracleSchedule Schedule(
        SplitMix64 rng,
        DateOnly from,
        OracleRounding rounding,
        OracleCostCategory category,
        decimal tEntry,
        decimal tExit) =>
        new(from, 18m, rounding, Rates(rng, category, tEntry, tExit, delivery: false), Rates(rng, category, tEntry, tExit, delivery: true));

    private static OracleRates Rates(SplitMix64 rng, OracleCostCategory category, decimal tEntry, decimal tExit, bool delivery)
    {
        decimal lo = Math.Max(1m, Math.Min(tEntry, tExit));
        decimal hi = Math.Max(1m, Math.Max(tEntry, tExit));
        decimal pct = BrokeragePcts[rng.Range(0, BrokeragePcts.Length - 1)];
        decimal bMin;
        decimal bCap;
        switch (category)
        {
            case OracleCostCategory.BrokerageAtMin:
                bMin = Math.Ceiling(hi * pct / 100m) + rng.Range(1, 10);
                bCap = bMin + rng.Range(0, 50);
                break;
            case OracleCostCategory.BrokerageAtCap:
                pct = Math.Min(5m, Math.Max(pct, Math.Ceiling(30000m / lo) / 100m));
                bMin = 0m;
                bCap = Math.Max(1m, Math.Floor(lo * pct / 100m * 0.8m));
                break;
            case OracleCostCategory.BrokerageBetween:
                bMin = Math.Floor(lo * pct / 100m * 0.5m);
                bCap = Math.Ceiling(hi * pct / 100m * 1.5m) + 1m;
                break;
            default:
                if (rng.Chance(10))
                {
                    pct = 0m;
                    bMin = 0m;
                    bCap = 0m;
                }
                else
                {
                    bMin = rng.Chance(50) ? 0m : rng.Range(1, 8);
                    bCap = bMin + rng.Range(1, 40);
                }

                break;
        }

        return new OracleRates(
            pct,
            bCap,
            bMin,
            SttPcts[rng.Range(0, SttPcts.Length - 1)],
            SttPcts[rng.Range(0, SttPcts.Length - 1)],
            ExchangePcts[rng.Range(0, ExchangePcts.Length - 1)],
            rng.Chance(50) ? 10m : 50m,
            Stamps[rng.Range(0, Stamps.Length - 1)],
            Others[rng.Range(0, Others.Length - 1)],
            delivery ? Dps[rng.Range(0, Dps.Length - 1)] : 0m);
    }
}

using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// A second, independent writing of the rules of D8 and D11 (ADR 0042) that the tests keep next to the production code: a straight reading of the spec table with switches that break one rule each. With no switch it must agree with the
/// production code on every leg and every quantity (a cross-check); with a switch it is a mutant, and the worked examples and the properties must catch it (criterion 23, the negative controls). Test code only.
/// </summary>
internal static class ReferenceCosting
{
    /// <summary>The ways a mutant breaks the rules of D8, D6 and D11.</summary>
    [Flags]
    public enum Break
    {
        None = 0,
        StampOnSells = 1,
        GstOnStt = 2,
        IgnoreBrokerageCap = 4,
        TotalRoundedOnce = 8,
        RoundQuantity = 16,
        DpOnBuys = 32,
        GstOnAllLines = 64,
        SttOnWrongSide = 128,
    }

    /// <summary>The legs of the sheet, a rate at a time, written the way the table of D8 reads.</summary>
    public static LegCharges Leg(CostSheet sheet, CostSegment segment, Side side, decimal turnover, DateOnly date, Break mutant = Break.None)
    {
        var schedule = sheet.RatesOn(date).Value;
        var r = schedule.RatesOf(segment);
        var buy = side == Side.Buy;
        var rounding = schedule.Rounding;

        decimal Rnd(decimal x) => mutant.HasFlag(Break.TotalRoundedOnce) ? x : CostModelRound(x, rounding);

        var percentBrokerage = turnover * r.BrokeragePct / 100m;
        var brokerage = r.BrokerageCap == 0m ? 0m : Rnd(mutant.HasFlag(Break.IgnoreBrokerageCap) ? Math.Max(r.BrokerageMin, percentBrokerage) : Math.Min(r.BrokerageCap, Math.Max(r.BrokerageMin, percentBrokerage)));
        var sttRate = mutant.HasFlag(Break.SttOnWrongSide) ? (buy ? r.SttSellPct : r.SttBuyPct) : (buy ? r.SttBuyPct : r.SttSellPct);
        var stt = Rnd(turnover * sttRate / 100m);
        var exchange = Rnd(turnover * r.ExchangeTxnPct / 100m);
        var sebi = Rnd(turnover * r.SebiPerCrore / 10_000_000m);
        var stamp = buy || mutant.HasFlag(Break.StampOnSells) ? Rnd(turnover * r.StampBuyPct / 100m) : 0m;
        var other = Rnd(turnover * r.OtherPct / 100m);
        var dpApplies = segment == CostSegment.Delivery && (!buy || mutant.HasFlag(Break.DpOnBuys));
        var dp = dpApplies ? r.DpChargePerSell : 0m;
        var gstBase = brokerage + exchange + sebi + (mutant.HasFlag(Break.GstOnStt) ? stt : 0m) + (mutant.HasFlag(Break.GstOnAllLines) ? stamp + other + dp : 0m);
        var gst = Rnd(schedule.GstPct / 100m * gstBase);
        return new LegCharges(brokerage, stt, exchange, sebi, stamp, other, dp, gst);
    }

    /// <summary>The leg total; a mutant that rounds the total once takes it from the unrounded lines.</summary>
    public static decimal Total(LegCharges leg, Break mutant = Break.None) => mutant.HasFlag(Break.TotalRoundedOnce) ? Math.Round(leg.Total, 2, MidpointRounding.AwayFromZero) : leg.Total;

    /// <summary>The quantity of D11; a mutant rounds instead of cutting.</summary>
    public static long Quantity(decimal capital, SizingProfile profile, decimal entry, decimal risk, Break mutant = Break.None)
    {
        var byRisk = capital * profile.RiskPerTradePct / 100m / risk;
        var byCapital = capital * profile.MaxCapitalPerTradePct / 100m / entry;
        var least = Math.Min(byRisk, byCapital);
        return mutant.HasFlag(Break.RoundQuantity) ? (long)Math.Round(least, MidpointRounding.AwayFromZero) : (long)Math.Floor(least);
    }

    private static decimal CostModelRound(decimal x, ChargeRounding rounding) => rounding switch
    {
        ChargeRounding.HalfAwayFromZero => Math.Round(x, 2, MidpointRounding.AwayFromZero),
        ChargeRounding.HalfEven => Math.Round(x, 2, MidpointRounding.ToEven),
        _ => Math.Round(x, 2, MidpointRounding.ToZero),
    };
}

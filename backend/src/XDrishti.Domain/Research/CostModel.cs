using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// The charge of one order (ADR 0042, D6 to D9): eight lines computed in decimal from the turnover of the order and the rates in force on its date, each rounded to the paisa once by the schedule's rule. It is a charge on a simulated order and
/// says nothing about edge, tax on trading income, margin or funding. Pure.
/// </summary>
public static class CostModel
{
    private const decimal Crore = 10_000_000m;

    /// <summary>
    /// Charges one leg. <paramref name="legSide"/> is the side of the order itself (the entry of a SELL trade is a sell order). <c>costs.no_rates_for_date</c> when the date is before the first schedule.
    /// </summary>
    /// <exception cref="ArgumentNullException">The sheet is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The turnover is negative, or the segment or side is unknown.</exception>
    public static Result<LegCharges> Leg(CostSheet sheet, CostSegment segment, Side legSide, decimal turnover, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentOutOfRangeException.ThrowIfNegative(turnover);
        if (!Enum.IsDefined(legSide))
        {
            throw new ArgumentOutOfRangeException(nameof(legSide), legSide, "Unknown side.");
        }

        var schedule = sheet.RatesOn(date);
        if (schedule.IsFailure)
        {
            return Result.Failure<LegCharges>(schedule.Error);
        }

        var rounding = schedule.Value.Rounding;
        var rates = schedule.Value.RatesOf(segment);
        var buy = legSide == Side.Buy;

        var brokerage = rates.BrokerageCap == 0m ? 0m : Round(Math.Min(rates.BrokerageCap, Math.Max(rates.BrokerageMin, turnover * rates.BrokeragePct / 100m)), rounding);
        var stt = Round(turnover * (buy ? rates.SttBuyPct : rates.SttSellPct) / 100m, rounding);
        var exchange = Round(turnover * rates.ExchangeTxnPct / 100m, rounding);
        var sebi = Round(turnover * rates.SebiPerCrore / Crore, rounding);
        var stamp = buy ? Round(turnover * rates.StampBuyPct / 100m, rounding) : 0m;
        var other = Round(turnover * rates.OtherPct / 100m, rounding);
        var dp = segment == CostSegment.Delivery && !buy ? rates.DpChargePerSell : 0m;
        var gst = Round(schedule.Value.GstPct / 100m * (brokerage + exchange + sebi), rounding);
        return Result.Success(new LegCharges(brokerage, stt, exchange, sebi, stamp, other, dp, gst));
    }

    /// <summary>The amount rounded to the paisa by the rule; the amount is never negative.</summary>
    internal static decimal Round(decimal amount, ChargeRounding rounding) => rounding switch
    {
        ChargeRounding.HalfAwayFromZero => Math.Round(amount, 2, MidpointRounding.AwayFromZero),
        ChargeRounding.HalfEven => Math.Round(amount, 2, MidpointRounding.ToEven),
        ChargeRounding.Down => Math.Round(amount, 2, MidpointRounding.ToZero),
        _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown rounding rule."),
    };
}

using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// Sizes and charges one gross trade (ADR 0042, D13): the quantity from the exact entry and the stop, the two orders charged on the filled prices and the dates of their own, <c>CostR</c> against <c>MaxCostR</c> in decimal, and net R from
/// the filled R. A pure function of one <see cref="SimTrade"/> and a <see cref="CostContext"/>: the costing can be recomputed from the stored record. The replay calls it after the window cut; the walker knows nothing of it.
/// </summary>
public static class TradeCostApplier
{
    private const int PriceDecimals = 8;

    /// <summary>
    /// Costs the trade. Quantity under 1 is <see cref="TradeSkip.QuantityBelowOne"/>; then a charge in R above <see cref="CostContext.MaxCostR"/> is <see cref="TradeSkip.CostAboveMax"/> (a charge equal to it is kept). A kept trade is the
    /// walker's trade byte for byte except <see cref="SimTrade.Costs"/> and <see cref="SimTrade.Costing"/>.
    /// </summary>
    /// <returns>The outcome; <c>costs.no_rates_for_date</c> when a leg's date is before the first schedule of the sheet; <c>costs.sizing_input_invalid</c> for a trade with no risk (a caller error: the walker makes none).</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The trade has been costed already.</exception>
    public static Result<CostApplication> Apply(SimTrade trade, CostContext context, CostSegment segment)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(context);
        if (trade.Costing is not null || trade.Costs != CostBasis.None)
        {
            throw new ArgumentException("The trade has been costed already.", nameof(trade));
        }

        var entry = Price(trade.EntryPrice);
        var risk = Math.Abs(entry - Price(trade.StopPrice));
        var sized = Sizing.Quantity(context.Capital, context.Profile, entry, risk);
        if (sized.IsFailure)
        {
            return Result.Failure<CostApplication>(sized.Error);
        }

        var quantity = sized.Value;
        if (quantity < 1)
        {
            return Result.Success(CostApplication.Skipped(TradeSkip.QuantityBelowOne));
        }

        if (trade.Legs is { } legs)
        {
            return ApplyLegs(trade, legs, context, segment, quantity, risk);
        }

        var entryTurnover = quantity * Price(trade.EntryFilledPrice);
        var exitTurnover = quantity * Price(trade.ExitFilledPrice);
        var exitSide = trade.Side == Side.Buy ? Side.Sell : Side.Buy;
        var entryLeg = CostModel.Leg(context.Sheet, segment, trade.Side, entryTurnover, trade.TradeDate);
        if (entryLeg.IsFailure)
        {
            return Result.Failure<CostApplication>(entryLeg.Error);
        }

        var exitLeg = CostModel.Leg(context.Sheet, segment, exitSide, exitTurnover, SessionClock.DateOf(trade.ExitEpochSeconds));
        if (exitLeg.IsFailure)
        {
            return Result.Failure<CostApplication>(exitLeg.Error);
        }

        var totalCost = entryLeg.Value.Total + exitLeg.Value.Total;
        var riskTaken = quantity * risk;
        var costR = totalCost / riskTaken;
        if (costR > context.MaxCostR)
        {
            return Result.Success(CostApplication.Skipped(TradeSkip.CostAboveMax));
        }

        var costRDouble = costR == 0m ? 0d : (double)costR;
        var net = trade.RFilled - costRDouble;
        var costing = new TradeCosting(segment, context.Sheet.Id, quantity, riskTaken, entryTurnover, exitTurnover, entryLeg.Value, exitLeg.Value, totalCost, costRDouble, net == 0 ? 0d : net);
        return Result.Success(CostApplication.Kept(trade with { Costs = CostBasis.Charged, Costing = costing }));
    }

    /// <summary>
    /// A trade with legs (P4-01, D11): every leg is one order on the schedule in force on its date. The quantity of a leg is <c>floor(quantity × share)</c>; the last exit leg takes what is left of the shares in, so that the quantities
    /// sum exactly; a leg that would have no shares is merged into the next one and not charged. <c>TotalCost</c> is the sum of all orders, <c>CostR</c> is against <c>quantity × R0</c>.
    /// </summary>
    private static Result<CostApplication> ApplyLegs(SimTrade trade, ValueList<Leg> legs, CostContext context, CostSegment segment, long quantity, decimal risk)
    {
        var exitSide = trade.Side == Side.Buy ? Side.Sell : Side.Buy;
        var quantities = new long[legs.Count];
        long entered = 0;
        for (var i = 0; i < legs.Count; i++)
        {
            if (legs[i].Kind != LegKind.Exit)
            {
                quantities[i] = (long)Math.Floor(quantity * legs[i].Share);
                entered += quantities[i];
            }
        }

        // P4-04, D8: the first fill of a pyramid holds only part of the full size; if that is no whole share the trade is a skipped quantity, as for a trade that enters one fill.
        if (entered < 1 || quantities[0] < 1)
        {
            return Result.Success(CostApplication.Skipped(TradeSkip.QuantityBelowOne));
        }

        var lastExit = -1;
        for (var i = 0; i < legs.Count; i++)
        {
            if (legs[i].Kind == LegKind.Exit)
            {
                lastExit = i;
            }
        }

        if (lastExit < 0)
        {
            return Result.Failure<CostApplication>(CostErrors.LegsInvalid("it has no exit leg"));
        }

        // A non-last exit leg whose floor(quantity × share) is 0 is dropped here and not charged; it is counted as a merged leg by the replay (legs minus charged legs, ADR 0050).
        var exited = 0L;
        for (var i = 0; i < legs.Count; i++)
        {
            if (legs[i].Kind == LegKind.Exit && i != lastExit)
            {
                quantities[i] = (long)Math.Floor(quantity * legs[i].Share);
                exited += quantities[i];
            }
        }

        quantities[lastExit] = entered - exited;
        var charged = new List<LegCharge>(legs.Count);
        LegCharges entryLines = LegCharges.Zero, exitLines = LegCharges.Zero;
        decimal entryTurnover = 0m, exitTurnover = 0m;
        for (var i = 0; i < legs.Count; i++)
        {
            if (quantities[i] < 1)
            {
                continue;
            }

            var isExit = legs[i].Kind == LegKind.Exit;
            var turnover = quantities[i] * Price(legs[i].FilledPrice);
            var lines = CostModel.Leg(context.Sheet, segment, isExit ? exitSide : trade.Side, turnover, SessionClock.DateOf(legs[i].EpochSeconds));
            if (lines.IsFailure)
            {
                return Result.Failure<CostApplication>(lines.Error);
            }

            charged.Add(new LegCharge(legs[i].EpochSeconds, quantities[i], turnover, lines.Value));
            if (isExit)
            {
                exitLines = exitLines.Plus(lines.Value);
                exitTurnover += turnover;
            }
            else
            {
                entryLines = entryLines.Plus(lines.Value);
                entryTurnover += turnover;
            }
        }

        var totalCost = entryLines.Total + exitLines.Total;
        var riskTaken = quantity * risk;
        var costR = totalCost / riskTaken;
        if (costR > context.MaxCostR)
        {
            return Result.Success(CostApplication.Skipped(TradeSkip.CostAboveMax));
        }

        var costRDouble = costR == 0m ? 0d : (double)costR;
        var net = trade.RFilled - costRDouble;
        var costing = new TradeCosting(segment, context.Sheet.Id, quantity, riskTaken, entryTurnover, exitTurnover, entryLines, exitLines, totalCost, costRDouble, net == 0 ? 0d : net, new ValueList<LegCharge>(charged));
        return Result.Success(CostApplication.Kept(trade with { Costs = CostBasis.Charged, Costing = costing }));
    }

    /// <summary>A price field of the trade as the decimal the walker computed it from (ADR 0038, T3): rounded to 8 places.</summary>
    private static decimal Price(double price) => Math.Round((decimal)price, PriceDecimals);
}

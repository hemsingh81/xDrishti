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

    /// <summary>A price field of the trade as the decimal the walker computed it from (ADR 0038, T3): rounded to 8 places.</summary>
    private static decimal Price(double price) => Math.Round((decimal)price, PriceDecimals);
}

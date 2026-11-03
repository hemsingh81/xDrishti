using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The charges of a pair trade (P14-13): four fills, each through <see cref="CostModel.Leg"/> for intraday cash equity on the date of the fill (the owner's sheet), summed. Without a sheet the trade stays gross and says so; a date before the sheet's first schedule leaves the net unavailable
/// (<c>costs_unavailable</c>), never zero charges. R is then net (or, when there is none, gross) rupees over the rupee risk. Pure.
/// </summary>
public static class PairCosting
{
    /// <summary>The trade with its net rupees and R, or labelled gross. <paramref name="costsUnavailable"/> is true when the sheet could not charge the trade.</summary>
    public static PairTrade Apply(PairTrade trade, CostSheet? sheet, out bool costsUnavailable)
    {
        ArgumentNullException.ThrowIfNull(trade);
        costsUnavailable = false;
        var labels = trade.Labels.Where(l => l is not ("gross" or "costs_unavailable")).ToList();
        if (sheet is null)
        {
            labels.Add("gross");
            return trade with { NetInr = null, R = PairTradeRule.R(trade.GrossInr, trade.RiskInr), Labels = labels };
        }

        var long_ = trade.Side == PairSide.LongSpread;
        var entry = SessionClock.DateOf(trade.EntryEpochSeconds);
        var exit = SessionClock.DateOf(trade.ExitEpochSeconds);
        var legs = new (Side Side, decimal Turnover, DateOnly Date)[]
        {
            (long_ ? Side.Buy : Side.Sell, trade.EntryY * trade.QtyY, entry),
            (long_ ? Side.Sell : Side.Buy, trade.ExitY * trade.QtyY, exit),
            (long_ ? Side.Sell : Side.Buy, trade.EntryX * trade.QtyX, entry),
            (long_ ? Side.Buy : Side.Sell, trade.ExitX * trade.QtyX, exit),
        };
        var charges = 0m;
        foreach (var (side, turnover, date) in legs)
        {
            var leg = CostModel.Leg(sheet, CostSegment.IntradayEquity, side, turnover, date);
            if (leg.IsFailure)
            {
                costsUnavailable = true;
                labels.Add("costs_unavailable");
                return trade with { NetInr = null, R = PairTradeRule.R(trade.GrossInr, trade.RiskInr), Labels = labels };
            }

            charges += leg.Value.Total;
        }

        var net = trade.GrossInr - charges;
        return trade with { NetInr = net, R = PairTradeRule.R(net, trade.RiskInr), Labels = labels };
    }
}

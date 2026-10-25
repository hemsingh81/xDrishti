using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The rupees and R of a paper trade at the owner's quantity (spec P10-01, criterion 5). Pure. Prices are the replay's filled prices to 8 decimals (as the cost applier reads them); charges are the sum of <see cref="CostModel.Leg"/> for the entry
/// order and the exit order on the dates of the fills, in the segment the replay used; R is the replay's, which does not depend on size.
/// </summary>
public static class PaperPnl
{
    private const int PriceDecimals = 8;

    /// <summary>Rupees before charges: <c>(exit - entry) x qty</c> for a buy, <c>(entry - exit) x qty</c> for a sell, to the paisa.</summary>
    public static decimal Gross(Side side, int qty, decimal entryFilled, decimal exitFilled)
    {
        var move = side == Side.Buy ? exitFilled - entryFilled : entryFilled - exitFilled;
        return Math.Round(move * qty, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The charges of the two orders at <paramref name="qty"/> on the sheet; the sheet's failure when it has no rates for a leg's date.</summary>
    public static Result<decimal> Charges(CostSheet sheet, CostSegment segment, Side side, int qty, decimal entryFilled, decimal exitFilled, DateOnly entryDate, DateOnly exitDate)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var exitSide = side == Side.Buy ? Side.Sell : Side.Buy;
        var entry = CostModel.Leg(sheet, segment, side, qty * entryFilled, entryDate);
        if (entry.IsFailure)
        {
            return Result.Failure<decimal>(entry.Error);
        }

        var exit = CostModel.Leg(sheet, segment, exitSide, qty * exitFilled, exitDate);
        return exit.IsFailure ? Result.Failure<decimal>(exit.Error) : Result.Success(entry.Value.Total + exit.Value.Total);
    }

    /// <summary>
    /// The exit of a closed replay trade at the account quantity. <paramref name="sheet"/> is the configured sheet (null: gross); the trade is charged only when it carries the replay's costing, so R is the grade's R either way.
    /// </summary>
    public static Result<PaperExit> Exit(SimTrade trade, int qty, CostSheet? sheet)
    {
        ArgumentNullException.ThrowIfNull(trade);
        var entry = Price(trade.EntryFilledPrice);
        var exit = Price(trade.ExitFilledPrice);
        var gross = Gross(trade.Side, qty, entry, exit);
        decimal? charges = null;
        decimal? net = null;
        string? sheetId = null;
        double? rNet = null;
        var basis = PaperWording.Gross;
        if (sheet is not null && trade.Costs == CostBasis.Charged && trade.Costing is { } costing) // the grader's own condition for "charged"
        {
            var paid = Charges(sheet, costing.Segment, trade.Side, qty, entry, exit, trade.TradeDate, SessionClock.DateOf(trade.ExitEpochSeconds));
            if (paid.IsFailure)
            {
                return Result.Failure<PaperExit>(paid.Error);
            }

            charges = paid.Value;
            net = gross - paid.Value;
            sheetId = sheet.Id;
            rNet = costing.RNet;
            basis = PaperWording.Net(sheet.Id);
        }

        return Result.Success(new PaperExit(
            DateTimeOffset.FromUnixTimeSeconds(trade.ExitEpochSeconds), exit, trade.ExitReason.ToString(), trade.SessionsHeld, gross, charges, sheetId, net, basis,
            trade.RExact, trade.RFilled, rNet, rNet ?? trade.RFilled, rNet is null ? "gross" : "net", trade.MaeR, trade.MfeR));
    }

    /// <summary>A replay price as a decimal to 8 places, as the cost applier reads it.</summary>
    public static decimal Price(double price) => Math.Round((decimal)price, PriceDecimals);
}

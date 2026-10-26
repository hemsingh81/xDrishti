using XDrishti.Domain.Market;

namespace XDrishti.Domain.Paper;

/// <summary>
/// An open paper position with its mark (spec P10-02). The unrealised rupees exist only when the mark is a value; they are null otherwise, never 0. Risk is to the initial stop (a trailing stop is not modelled).
/// </summary>
/// <param name="Trade">The open trade.</param>
/// <param name="Mark">The mark.</param>
/// <param name="Unrealised">Rupees at the mark; null when the mark is unavailable.</param>
/// <param name="RiskPctOfEquity">Risk as a percent of the account's equity; null when equity is not known or not positive.</param>
/// <param name="HeldSessions">Trading sessions from the entry session to the last closed one, both included.</param>
public sealed record PaperPosition(LedgerTrade Trade, PaperMark Mark, decimal? Unrealised, decimal? RiskPctOfEquity, int HeldSessions)
{
    /// <summary>The position with its figures.</summary>
    public static PaperPosition Of(LedgerTrade trade, PaperMark mark, decimal? equity, int heldSessions)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(mark);
        decimal? unrealised = mark.Price is { } price
            ? Math.Round((trade.IsBuy ? price - trade.EntryPrice : trade.EntryPrice - price) * trade.Qty, 2, MidpointRounding.AwayFromZero)
            : null;
        decimal? pct = equity is > 0 ? trade.RiskAmount / equity.Value * 100m : null;
        return new PaperPosition(trade, mark, unrealised, pct, heldSessions);
    }

    /// <summary>The India date of the entry.</summary>
    public DateOnly EntryDate => IndiaTime.Today(Trade.EntryAt);
}

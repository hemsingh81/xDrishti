namespace XDrishti.Domain.Paper;

/// <summary>The stored exit of a paper trade as the ledger reads it (spec P10-02): instant, rupees and R. Plain data.</summary>
/// <param name="ExitAt">The start of the exit minute.</param>
/// <param name="GrossPnl">Rupees before charges at the owner's quantity.</param>
/// <param name="Charges">Rupees of charges by the stored sheet; null when no sheet charged the trade.</param>
/// <param name="NetPnl">Gross less charges; null when no sheet charged the trade.</param>
/// <param name="ChargeSheetId">The sheet that charged; null without one.</param>
/// <param name="RSim">The trade's R (the grade's R).</param>
public sealed record LedgerExit(DateTimeOffset ExitAt, decimal GrossPnl, decimal? Charges, decimal? NetPnl, string? ChargeSheetId, double RSim)
{
    /// <summary>The trade's rupee result: after the stored sheet's charges when it was charged, else gross.</summary>
    public decimal Result => NetPnl ?? GrossPnl;

    /// <summary>Whether a stored sheet charged this trade.</summary>
    public bool Charged => NetPnl is not null;
}

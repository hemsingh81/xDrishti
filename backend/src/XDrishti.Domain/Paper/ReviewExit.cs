namespace XDrishti.Domain.Paper;

/// <summary>The stored exit of a paper trade as the review reads it (spec P10-03): rupees and the three R figures the execution waterfall needs.</summary>
/// <param name="ExitAt">The start of the exit minute.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="GrossPnl">Rupees before charges.</param>
/// <param name="Charges">Rupees of charges; null without a sheet.</param>
/// <param name="NetPnl">Gross less charges; null without a sheet.</param>
/// <param name="RExact">R from the exact prices.</param>
/// <param name="RGross">R from the filled prices.</param>
/// <param name="RNet">R after the sheet's charges; null without a sheet.</param>
/// <param name="RSim">The trade's R: net when charged, else gross.</param>
public sealed record ReviewExit(DateTimeOffset ExitAt, string ExitReason, decimal GrossPnl, decimal? Charges, decimal? NetPnl, double RExact, double RGross, double? RNet, double RSim)
{
    /// <summary>The rupee result: after the stored sheet's charges when charged, else gross.</summary>
    public decimal Result => NetPnl ?? GrossPnl;
}

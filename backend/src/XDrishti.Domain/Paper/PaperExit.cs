namespace XDrishti.Domain.Paper;

/// <summary>
/// The exit of a paper trade (spec P10-01): instant, filled price, reason, rupees gross and net of the stored charge sheet and R. Written once, when the replay closes the trade.
/// </summary>
/// <param name="ExitAt">The start of the exit minute.</param>
/// <param name="ExitPrice">The filled exit.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="HeldSessions">Sessions from the entry session to the exit session, both included.</param>
/// <param name="GrossPnl">Rupees before charges at the account quantity.</param>
/// <param name="Charges">Rupees of charges for the two orders at the account quantity; null without a sheet.</param>
/// <param name="ChargeSheetId">The sheet that charged; null without one.</param>
/// <param name="NetPnl">Gross less charges; null without a sheet.</param>
/// <param name="CostBasis">What the figures are net of.</param>
/// <param name="RExact">R from the exact prices.</param>
/// <param name="RGross">R from the filled prices.</param>
/// <param name="RNet">R after the sheet's charges; null without a sheet.</param>
/// <param name="RSim">The trade's R: net when charged, else gross (the grade's R).</param>
/// <param name="RBasis">net or gross.</param>
/// <param name="MaeR">Maximum adverse excursion in R.</param>
/// <param name="MfeR">Maximum favourable excursion in R.</param>
public sealed record PaperExit(
    DateTimeOffset ExitAt,
    decimal ExitPrice,
    string ExitReason,
    int HeldSessions,
    decimal GrossPnl,
    decimal? Charges,
    string? ChargeSheetId,
    decimal? NetPnl,
    string CostBasis,
    double RExact,
    double RGross,
    double? RNet,
    double RSim,
    string RBasis,
    double MaeR,
    double MfeR);

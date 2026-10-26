namespace XDrishti.Domain.Paper;

/// <summary>One paper account's figures as the plan reads them (spec P10-02, criterion 7). A null number is unavailable and the plan treats the limit as unknown (never a pass).</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Equity">Capital plus the realised result, rupees.</param>
/// <param name="OpenRiskPct">Open risk, percent of equity.</param>
/// <param name="FundsAvailable">Equity less the open notional (at least 0), rupees.</param>
/// <param name="DailyLossPct">The last closed session's realised loss as a positive percent of equity (0 when not a loss).</param>
/// <param name="HeldSymbols">The open paper symbols, upper case.</param>
public sealed record PaperAccountBook(Guid AccountId, double? Equity, double? OpenRiskPct, double? FundsAvailable, double? DailyLossPct, IReadOnlySet<string> HeldSymbols);

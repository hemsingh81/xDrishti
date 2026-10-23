using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Checklist;

/// <summary>The account as the checklist sees it: mode and role, its risk profile limits and its state today. A null number is "not known" and makes its check unavailable.</summary>
/// <param name="AccountId">The account, as text.</param>
/// <param name="Mode">Paper or manual live. Only paper is accepted (spec P7-05).</param>
/// <param name="IsTrading">Whether the account has the Trading role.</param>
/// <param name="MaxPerTradeRiskPct">Profile: risk of one trade, percent.</param>
/// <param name="MaxTotalOpenRiskPct">Profile: total open risk, percent.</param>
/// <param name="MaxPerSector">Profile: positions per sector.</param>
/// <param name="DailyLossLimitPct">Profile: daily loss limit, percent.</param>
/// <param name="FundsAvailable">Money the account can use, in rupees.</param>
/// <param name="OpenRiskPct">Open risk now, percent of capital.</param>
/// <param name="SectorPositions">Open trades plus approved tickets in the ticket's sector.</param>
/// <param name="DailyLossPct">Loss so far today as a positive percent (0 when none).</param>
public sealed record ChecklistAccount(
    string AccountId,
    TradingMode Mode,
    bool IsTrading,
    double? MaxPerTradeRiskPct,
    double? MaxTotalOpenRiskPct,
    int? MaxPerSector,
    double? DailyLossLimitPct,
    double? FundsAvailable,
    double? OpenRiskPct,
    int? SectorPositions,
    double? DailyLossPct);

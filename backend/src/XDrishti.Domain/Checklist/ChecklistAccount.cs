using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Checklist;

/// <summary>The account as the checklist sees it: mode and role, its risk profile limits and its state today. A null number is "not known" and makes its check unavailable.</summary>
/// <param name="AccountId">The account, as text.</param>
/// <param name="Mode">Paper or manual live. A live account is judged with the live-ready gate in place of the paper gate (spec P11-04).</param>
/// <param name="IsTrading">Whether the account has the Trading role.</param>
/// <param name="MaxPerTradeRiskPct">Profile: risk of one trade, percent.</param>
/// <param name="MaxTotalOpenRiskPct">Profile: total open risk, percent.</param>
/// <param name="MaxPerSector">Profile: positions per sector.</param>
/// <param name="DailyLossLimitPct">Profile: daily loss limit, percent.</param>
/// <param name="FundsAvailable">Money the account can use, in rupees.</param>
/// <param name="OpenRiskPct">Open risk now, percent of capital.</param>
/// <param name="SectorPositions">Open trades plus approved tickets in the ticket's sector.</param>
/// <param name="DailyLossPct">Loss so far today as a positive percent (0 when none).</param>
/// <param name="LiveReady">For a manual-live account, the facts of its live-ready gate; null when no source gives them (then the gate is unavailable, never a pass).</param>
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
    double? DailyLossPct,
    LiveReadyFacts? LiveReady = null);

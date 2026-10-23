using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Allocation;

/// <summary>An account as the allocator sees it: gate facts, its risk profile numbers and its state. A null number is "not known" and nothing is allocated to the account.</summary>
/// <param name="AccountId">The broker account (the key of <c>plan_item_allocations</c>).</param>
/// <param name="Mode">Paper or manual live. Only paper is ever allocated.</param>
/// <param name="IsTrading">Whether the account has the Trading role.</param>
/// <param name="Equity">Capital the percentages apply to, in rupees (fixed capital, or the funds the last sync read).</param>
/// <param name="RiskPerTradePct">Profile (with overrides): risk of one trade, percent of equity.</param>
/// <param name="MaxCapitalPerTradePct">Profile: most capital in one trade, percent of equity.</param>
/// <param name="MaxTotalRiskPct">Profile: total open risk, percent of equity.</param>
/// <param name="OpenRiskPct">Open risk now, percent of equity (0 when none).</param>
/// <param name="FundsAvailable">Money the account can use, in rupees.</param>
/// <param name="HoldsSymbol">Whether the account already holds the symbol.</param>
public sealed record AllocationAccount(
    Guid AccountId,
    TradingMode Mode,
    bool IsTrading,
    double? Equity,
    double? RiskPerTradePct,
    double? MaxCapitalPerTradePct,
    double? MaxTotalRiskPct,
    double? OpenRiskPct,
    double? FundsAvailable,
    bool? HoldsSymbol);

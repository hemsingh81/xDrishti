namespace XDrishti.Domain.Accounts;

/// <summary>Per-account replacements for values of the shared risk profile; null means "use the profile's value".</summary>
public sealed record TradingOverrides(decimal? RiskPerTradePct, int? MaxNewEntriesPerDay, int? MaxOpenPositions, decimal? DailyLossPct)
{
    public static readonly TradingOverrides None = new(null, null, null, null);
}

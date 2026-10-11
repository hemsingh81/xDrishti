using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// The numbers of a risk profile (docs/design/16-configuration.md, risk_profiles). Percentages are in percent (0.5 means 0.5 %),
/// "R" values are multiples of the risk taken per trade.
/// </summary>
public sealed record RiskLimits(
    decimal RiskPerTradePct,
    decimal MaxCapitalPerTradePct,
    int MaxNewEntriesPerDay,
    int MaxOpenPositions,
    decimal MaxTotalRiskPct,
    int MaxPerSector,
    decimal MaxPairCorrelation,
    int MaxIntraday,
    int MaxSwing,
    decimal DailyLossR,
    decimal DailyLossPct,
    decimal WeeklyLossR,
    WeeklyLossAction WeeklyLossAction)
{
    /// <summary>Risk per trade, as a percentage of capital: more than 0, at most 2.</summary>
    public const decimal MaxRiskPerTradePct = 2m;

    public const int MaxCount = 100;

    /// <summary>Daily loss limit as a percentage of capital: more than 0, at most 100.</summary>
    public const decimal MaxLossPct = 100m;

    public const decimal MaxLossR = 100m;

    /// <summary>Checks every value; the returned collection is empty when the limits are valid.</summary>
    public FieldErrors Validate()
    {
        var errors = new FieldErrors();
        Ranges.GreaterThanUpTo(errors, "riskPerTradePct", RiskPerTradePct, 0m, MaxRiskPerTradePct);
        Ranges.GreaterThanUpTo(errors, "maxCapitalPerTradePct", MaxCapitalPerTradePct, 0m, 100m);
        Count(errors, "maxNewEntriesPerDay", MaxNewEntriesPerDay);
        Count(errors, "maxOpenPositions", MaxOpenPositions);
        Ranges.GreaterThanUpTo(errors, "maxTotalRiskPct", MaxTotalRiskPct, 0m, 100m);
        if (MaxTotalRiskPct < RiskPerTradePct)
        {
            errors.Add("maxTotalRiskPct", "Must be at least the risk per trade.");
        }

        Count(errors, "maxPerSector", MaxPerSector);
        Ranges.Between(errors, "maxPairCorrelation", MaxPairCorrelation, 0m, 1m);
        Count(errors, "maxIntraday", MaxIntraday);
        Count(errors, "maxSwing", MaxSwing);
        Ranges.GreaterThanUpTo(errors, "dailyLossR", DailyLossR, 0m, MaxLossR);
        Ranges.GreaterThanUpTo(errors, "dailyLossPct", DailyLossPct, 0m, MaxLossPct);
        Ranges.GreaterThanUpTo(errors, "weeklyLossR", WeeklyLossR, 0m, MaxLossR);
        if (!Enum.IsDefined(WeeklyLossAction))
        {
            errors.Add("weeklyLossAction", "Unknown action.");
        }

        return errors;
    }

    private static void Count(FieldErrors errors, string field, int value)
    {
        if (value is < 1 or > MaxCount)
        {
            errors.Add(field, $"Must be between 1 and {MaxCount}.");
        }
    }
}

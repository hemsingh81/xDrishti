using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>
/// A shared set of risk limits. Percentages are in percent; "R" values are multiples of the risk per trade. IsSeeded marks a built-in profile
/// (can be edited, not deleted); AccountCount is how many active accounts use it (a profile in use cannot be deleted);
/// WeeklyLossAction is "halve_size", "stop_new_entries" or "alert_only".
/// </summary>
public sealed record RiskProfileView(
    Guid Id,
    string Name,
    bool IsSeeded,
    int AccountCount,
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
    string WeeklyLossAction);

/// <summary>The values of a risk profile to create or to replace an existing one with. Every value is required.</summary>
public sealed record RiskProfileInput(
    string? Name,
    decimal? RiskPerTradePct,
    decimal? MaxCapitalPerTradePct,
    int? MaxNewEntriesPerDay,
    int? MaxOpenPositions,
    decimal? MaxTotalRiskPct,
    int? MaxPerSector,
    decimal? MaxPairCorrelation,
    int? MaxIntraday,
    int? MaxSwing,
    decimal? DailyLossR,
    decimal? DailyLossPct,
    decimal? WeeklyLossR,
    string? WeeklyLossAction);

internal static class RiskProfileMapping
{
    public static RiskProfileView ToView(RiskProfile profile, int accountCount) => new(
        profile.Id,
        profile.Name,
        profile.IsSeeded,
        accountCount,
        profile.RiskPerTradePct,
        profile.MaxCapitalPerTradePct,
        profile.MaxNewEntriesPerDay,
        profile.MaxOpenPositions,
        profile.MaxTotalRiskPct,
        profile.MaxPerSector,
        profile.MaxPairCorrelation,
        profile.MaxIntraday,
        profile.MaxSwing,
        profile.DailyLossR,
        profile.DailyLossPct,
        profile.WeeklyLossR,
        AccountCodes.Name(profile.WeeklyLossAction));

    /// <summary>The limits of a request; null (with errors) when a value is missing or a code is unknown. Ranges are checked by the domain.</summary>
    public static RiskLimits? ParseLimits(RiskProfileInput input, FieldErrors errors)
    {
        var before = errors.Items.Count;
        var action = WeeklyLossAction.HalveSize;
        if (input.WeeklyLossAction is null)
        {
            errors.Add("weeklyLossAction", "Required.");
        }
        else if (!AccountCodes.TryParseWeeklyLossAction(input.WeeklyLossAction, out action))
        {
            errors.Add("weeklyLossAction", "Use halve_size, stop_new_entries or alert_only.");
        }

        var limits = new RiskLimits(
            Need(input.RiskPerTradePct, "riskPerTradePct", errors),
            Need(input.MaxCapitalPerTradePct, "maxCapitalPerTradePct", errors),
            Need(input.MaxNewEntriesPerDay, "maxNewEntriesPerDay", errors),
            Need(input.MaxOpenPositions, "maxOpenPositions", errors),
            Need(input.MaxTotalRiskPct, "maxTotalRiskPct", errors),
            Need(input.MaxPerSector, "maxPerSector", errors),
            Need(input.MaxPairCorrelation, "maxPairCorrelation", errors),
            Need(input.MaxIntraday, "maxIntraday", errors),
            Need(input.MaxSwing, "maxSwing", errors),
            Need(input.DailyLossR, "dailyLossR", errors),
            Need(input.DailyLossPct, "dailyLossPct", errors),
            Need(input.WeeklyLossR, "weeklyLossR", errors),
            action);
        return errors.Items.Count == before ? limits : null;
    }

    private static T Need<T>(T? value, string field, FieldErrors errors)
        where T : struct
    {
        if (value is { } present)
        {
            return present;
        }

        errors.Add(field, "Required.");
        return default;
    }
}

using System.Globalization;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>A short text of what an edit changed, for the audit trail.</summary>
internal static class RiskProfileChanges
{
    public static string Describe(string oldName, RiskLimits oldLimits, RiskProfile after)
    {
        var now = after.Limits;
        var changes = new List<string>();
        if (oldName != after.Name)
        {
            changes.Add($"name '{oldName}' -> '{after.Name}'");
        }

        Add(changes, "riskPerTradePct", oldLimits.RiskPerTradePct, now.RiskPerTradePct);
        Add(changes, "maxCapitalPerTradePct", oldLimits.MaxCapitalPerTradePct, now.MaxCapitalPerTradePct);
        Add(changes, "maxNewEntriesPerDay", oldLimits.MaxNewEntriesPerDay, now.MaxNewEntriesPerDay);
        Add(changes, "maxOpenPositions", oldLimits.MaxOpenPositions, now.MaxOpenPositions);
        Add(changes, "maxTotalRiskPct", oldLimits.MaxTotalRiskPct, now.MaxTotalRiskPct);
        Add(changes, "maxPerSector", oldLimits.MaxPerSector, now.MaxPerSector);
        Add(changes, "maxPairCorrelation", oldLimits.MaxPairCorrelation, now.MaxPairCorrelation);
        Add(changes, "maxIntraday", oldLimits.MaxIntraday, now.MaxIntraday);
        Add(changes, "maxSwing", oldLimits.MaxSwing, now.MaxSwing);
        Add(changes, "dailyLossR", oldLimits.DailyLossR, now.DailyLossR);
        Add(changes, "dailyLossPct", oldLimits.DailyLossPct, now.DailyLossPct);
        Add(changes, "weeklyLossR", oldLimits.WeeklyLossR, now.WeeklyLossR);
        if (oldLimits.WeeklyLossAction != now.WeeklyLossAction)
        {
            changes.Add($"weeklyLossAction {AccountCodes.Name(oldLimits.WeeklyLossAction)} -> {AccountCodes.Name(now.WeeklyLossAction)}");
        }

        return changes.Count == 0
            ? $"Risk profile '{after.Name}' saved without changes"
            : $"Risk profile '{after.Name}': " + string.Join("; ", changes);
    }

    private static void Add<T>(List<string> changes, string field, T before, T after)
        where T : IFormattable, IEquatable<T>
    {
        if (!before.Equals(after))
        {
            changes.Add($"{field} {before.ToString("0.####", CultureInfo.InvariantCulture)} -> {after.ToString("0.####", CultureInfo.InvariantCulture)}");
        }
    }
}

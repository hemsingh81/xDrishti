namespace XDrishti.Domain.Replay;

/// <summary>The limits of the replay account, copied from a risk profile when the replay starts so the run does not depend on a profile that is edited later. Percentages are in percent.</summary>
/// <param name="RiskPerTradePct">Risk of one trade, percent of equity.</param>
/// <param name="MaxCapitalPerTradePct">Most capital in one trade, percent of equity.</param>
/// <param name="MaxTotalRiskPct">Total open risk, percent of equity.</param>
/// <param name="MaxPerSector">Positions per sector.</param>
/// <param name="DailyLossPct">Daily loss limit, percent of equity.</param>
public sealed record ReplayRiskProfile(double RiskPerTradePct, double MaxCapitalPerTradePct, double MaxTotalRiskPct, int MaxPerSector, double DailyLossPct)
{
    /// <summary>The seeded <c>standard</c> profile: 0.5 % risk, 25 % of capital, 3 % total open risk, 2 per sector, 1.5 % daily loss.</summary>
    public static ReplayRiskProfile Standard { get; } = new(0.5, 25, 3.0, 2, 1.5);

    /// <summary>The problems of the values; empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (!double.IsFinite(RiskPerTradePct) || RiskPerTradePct is <= 0 or > 2)
        {
            p.Add("riskPerTradePct must be above 0 and at most 2");
        }

        if (!double.IsFinite(MaxCapitalPerTradePct) || MaxCapitalPerTradePct is <= 0 or > 100)
        {
            p.Add("maxCapitalPerTradePct must be above 0 and at most 100");
        }

        if (!double.IsFinite(MaxTotalRiskPct) || MaxTotalRiskPct is <= 0 or > 100)
        {
            p.Add("maxTotalRiskPct must be above 0 and at most 100");
        }

        if (MaxPerSector is < 0 or > 100)
        {
            p.Add("maxPerSector must be from 0 to 100");
        }

        if (!double.IsFinite(DailyLossPct) || DailyLossPct is <= 0 or > 100)
        {
            p.Add("dailyLossPct must be above 0 and at most 100");
        }

        return p;
    }
}

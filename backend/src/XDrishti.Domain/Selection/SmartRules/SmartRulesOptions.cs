namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>Settings of the smart rules (doc 16 <c>smart_rules</c>, spec P7-06). Defaults are the documented ones; the settings home is P7-00 Q14.</summary>
/// <param name="GateEnabled">Switch of the market gate.</param>
/// <param name="VixReduce">India VIX at or above this reduces the plan.</param>
/// <param name="VixNoTrade">India VIX at or above this cancels the plan.</param>
/// <param name="BreadthMin">Share of tracked stocks above their 20-day average (0 to 1); below it reduces the plan.</param>
/// <param name="ReducedItemsFactor">Share of the items kept when reduced.</param>
/// <param name="ReducedRiskMultiplier">Risk factor when reduced.</param>
/// <param name="ThrottleEnabled">Switch of the equity-curve throttle.</param>
/// <param name="ThrottleWindowTrades">Number of taken trades the average covers.</param>
/// <param name="ThrottleCut">Risk factor while the equity is below its average.</param>
/// <param name="CorrelationEnabled">Switch of the correlation-aware selection.</param>
/// <param name="CorrelationMax">A pair above this (side-adjusted) correlation is the same bet.</param>
/// <param name="HonestyFraction">A rule that removed this share or more of the candidates it saw in this plan raises a flag.</param>
/// <param name="GateMaxAgeDays">Oldest market figure the gate accepts, in calendar days before the decision date. Default 4: the previous session, allowing for a weekend (no calendar in the Domain); older reads unavailable.</param>
public sealed record SmartRulesOptions(
    bool GateEnabled = true,
    double VixReduce = 18,
    double VixNoTrade = 22,
    double BreadthMin = 0.30,
    double ReducedItemsFactor = 0.5,
    double ReducedRiskMultiplier = 0.75,
    bool ThrottleEnabled = true,
    int ThrottleWindowTrades = 20,
    double ThrottleCut = 0.5,
    bool CorrelationEnabled = true,
    double CorrelationMax = 0.60,
    double HonestyFraction = 0.5,
    int GateMaxAgeDays = 4)
{
    /// <summary>What is wrong with these settings; empty when valid. <see cref="SmartRulesEngine"/> refuses invalid options.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        static bool Fraction(double v) => double.IsFinite(v) && v > 0 && v <= 1;
        if (!double.IsFinite(VixReduce) || !double.IsFinite(VixNoTrade) || VixReduce <= 0 || VixReduce >= VixNoTrade)
        {
            problems.Add("VixReduce must be positive and below VixNoTrade");
        }

        if (!Fraction(BreadthMin))
        {
            problems.Add("BreadthMin must be in (0, 1]");
        }

        if (!Fraction(ReducedItemsFactor))
        {
            problems.Add("ReducedItemsFactor must be in (0, 1]");
        }

        if (!Fraction(ReducedRiskMultiplier))
        {
            problems.Add("ReducedRiskMultiplier must be in (0, 1]");
        }

        if (!Fraction(ThrottleCut))
        {
            problems.Add("ThrottleCut must be in (0, 1]");
        }

        if (!Fraction(HonestyFraction))
        {
            problems.Add("HonestyFraction must be in (0, 1]");
        }

        if (!double.IsFinite(CorrelationMax) || CorrelationMax is <= 0 or > 1)
        {
            problems.Add("CorrelationMax must be in (0, 1]");
        }

        if (ThrottleWindowTrades < 1)
        {
            problems.Add("ThrottleWindowTrades must be at least 1");
        }

        if (GateMaxAgeDays < 1)
        {
            problems.Add("GateMaxAgeDays must be at least 1");
        }

        return problems;
    }
}

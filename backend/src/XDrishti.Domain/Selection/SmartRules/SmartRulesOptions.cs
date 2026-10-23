namespace XDrishti.Domain.Selection;

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
/// <param name="HonestyFraction">A rule that removed this share or more of the candidates it saw raises a flag.</param>
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
    double HonestyFraction = 0.5);

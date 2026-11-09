namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The only keys learning may ever propose a change to (AU-10, ADR 0242, ADR 0247). A key is here only when its descriptor declares a safer direction that the existing validation or an ADR makes tighten-only, and when a measured source can inform it
/// (the cost realism of ADR 0230, the decay trip, the readiness bars). A key with no direction is never learnable; <c>Confidence:Loop:Level</c> is not, and neither is any stored switch (Auto, the kill and readiness switches, the model switches).
/// </summary>
public static class LearnedAllowList
{
    public static IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Confidence:CostStress:TierABps",
        "Confidence:CostStress:TierBBps",
        "Confidence:CostStress:TierACutCrore",
        "Confidence:CostStress:TierBCutCrore",
        "Confidence:CostStress:RangeFraction",
        "Confidence:CostStress:MinTurnoverCrore",
        "Confidence:Decay:Sigmas",
        "Confidence:Readiness:ExpectancyR",
        "Confidence:Readiness:TStatistic",
    };
}

namespace XDrishti.Domain.Readiness;

/// <summary>The words and cut-offs of the four labels (spec CF-11 D4).</summary>
public static class ReadinessLabels
{
    /// <summary>The label of a score. A score exactly on a cut-off takes the higher label. A non-finite score is "Not ready".</summary>
    public static ReadinessLabel Of(double score, ReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(score))
        {
            return ReadinessLabel.NotReady;
        }

        return score >= options.ReadyFrom ? ReadinessLabel.ReadyForNextPhase
            : score >= options.UsableFrom ? ReadinessLabel.UsableWithAdvice
            : score >= options.BuildingFrom ? ReadinessLabel.Building
            : ReadinessLabel.NotReady;
    }

    /// <summary>The words shown to the owner.</summary>
    public static string Text(ReadinessLabel label) => label switch
    {
        ReadinessLabel.ReadyForNextPhase => "Ready for the next phase",
        ReadinessLabel.UsableWithAdvice => "Usable with advice",
        ReadinessLabel.Building => "Building",
        _ => "Not ready",
    };

    /// <summary>The stable code stored in a snapshot and sent by the API.</summary>
    public static string Code(ReadinessLabel label) => label switch
    {
        ReadinessLabel.ReadyForNextPhase => "ready",
        ReadinessLabel.UsableWithAdvice => "usable",
        ReadinessLabel.Building => "building",
        _ => "not_ready",
    };
}

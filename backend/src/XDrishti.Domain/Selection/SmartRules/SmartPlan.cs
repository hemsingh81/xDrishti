namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>The ranked list after the smart rules (spec P7-06).</summary>
/// <param name="Primary">Candidates that stay primary, in rank order.</param>
/// <param name="Reserve">Candidates moved to the reserve, in rank order, with reasons.</param>
/// <param name="Gate">The gate's answer.</param>
/// <param name="Throttle">The throttle's answer.</param>
/// <param name="Correlation">Status of the correlation rule.</param>
/// <param name="CorrelationUnavailablePairs">Pairs (candidate vs book or better ticket) that had no figure and were not checked.</param>
/// <param name="RiskMultiplier">Gate risk factor times throttle risk factor.</param>
/// <param name="Flags">Visible flags; the banner is made of them.</param>
/// <param name="MetaSizeMultiplierApplied">Always false: the meta-labelling size multiplier is advisory and never changes a size (ADR 0075).</param>
public sealed record SmartPlan(
    IReadOnlyList<RankedCandidate> Primary,
    IReadOnlyList<ReservedCandidate> Reserve,
    GateResult Gate,
    ThrottleResult Throttle,
    RuleStatus Correlation,
    int CorrelationUnavailablePairs,
    double RiskMultiplier,
    IReadOnlyList<PlanFlag> Flags,
    bool MetaSizeMultiplierApplied)
{
    /// <summary>The flags as banner lines.</summary>
    public IReadOnlyList<string> Banner => [.. Flags.Select(f => f.Text)];
}

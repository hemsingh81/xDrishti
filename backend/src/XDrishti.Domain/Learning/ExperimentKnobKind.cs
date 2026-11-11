namespace XDrishti.Domain.Learning;

/// <summary>What kind of assumption an experiment changes (P6-11, ADR 0072). The kind decides whether a cheaper value is allowed to win.</summary>
public enum ExperimentKnobKind
{
    /// <summary>A strategy or learning parameter (a stop multiple, a selection threshold, a half-life); the value is free text.</summary>
    Parameter,

    /// <summary>The exit policy; the value is a label.</summary>
    Exit,

    /// <summary>The slippage assumed in the replay, in basis points (a non-negative number). A lower value than the baseline's is a cheaper assumption and cannot win.</summary>
    Slippage,

    /// <summary>
    /// The scan scope (BK-11b): the hypothesis that a strategy should be read on one liquidity tier only. Only ever proposed from an observation; a scope experiment is not built, so it is never started and applies nothing (<see cref="HypothesisSource.Universe"/>).
    /// </summary>
    Scope,
}

namespace XDrishti.Domain.Confidence;

/// <summary>
/// How many independent trades a cell or a verdict rests on and what that means for seeing an edge (CF-01, ADR 0203). Every member except <see cref="N"/>, <see cref="Mode"/>, <see cref="Method"/> and <see cref="Note"/> is null when it cannot be computed; <see cref="Note"/> then says why
/// (<c>unavailable: &lt;reason&gt;</c>).
/// </summary>
/// <param name="Mode">The mode in force when this was computed.</param>
/// <param name="N">Trades.</param>
/// <param name="NEff">Effective (independent) trades, between 1 and N.</param>
/// <param name="DesignEffect">N over NEff.</param>
/// <param name="Method"><c>cluster</c>, <c>serial</c> or <c>none</c>.</param>
/// <param name="NeededTrades">Effective trades needed to see the target edge (a lower bound: normal R).</param>
/// <param name="MinDetectableEdgeR">The smallest edge in R that NEff effective trades could see.</param>
/// <param name="MinTrl">Minimum track-record length in iid trades.</param>
/// <param name="MinTrlDependent">MinTrl times the design effect: trades at this set's dependence.</param>
/// <param name="SpanYears">Years from the first to the last entry.</param>
/// <param name="YearsToEvidence">Years to reach NeededTrades at the rate NEff arrived, if the rate and the edge hold.</param>
/// <param name="EnoughByNEff">Whether ceil(NEff) reaches the minimum trades; null when NEff is unavailable (never a pass).</param>
/// <param name="NormalAssumed">MinTrl used skew 0 and kurtosis 3 because there were fewer than 20 trades.</param>
/// <param name="Note">Remarks and the unavailable reasons, joined by "; "; empty when there is nothing to say.</param>
/// <param name="TargetEdgeR">The edge in R that <paramref name="NeededTrades"/> is about.</param>
/// <param name="Settings">The settings that produced it (<see cref="ConfidenceOptions.KeyText"/>); null for a stored cell row (the snapshot key records them).</param>
public sealed record DependenceAssessment(
    ConfidenceMode Mode,
    int N,
    double? NEff,
    double? DesignEffect,
    string Method,
    int? NeededTrades,
    double? MinDetectableEdgeR,
    int? MinTrl,
    int? MinTrlDependent,
    double? SpanYears,
    double? YearsToEvidence,
    bool? EnoughByNEff,
    bool NormalAssumed,
    string Note,
    double? TargetEdgeR = null,
    string? Settings = null);

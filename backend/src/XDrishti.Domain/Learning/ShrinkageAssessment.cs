using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The shrunk figures of one cell window (CF-02, ADR 0204), shown beside the raw ones and changing none of them. When a figure cannot be computed every figure is null and <see cref="Note"/> reads <c>unavailable: &lt;reason&gt;</c>
/// (never a number, never a pass).
/// </summary>
/// <param name="Mode">The mode in force when this was computed.</param>
/// <param name="ParentLevel"><c>star</c> (the cell's own <c>*</c> cell without it), <c>strategy</c> or <c>system</c>; <c>regime</c> is reserved. Null when unavailable.</param>
/// <param name="ParentMeanR">The leave-one-out mean R of the parent.</param>
/// <param name="TauSquared">The between-cell variance used (R squared), never below the floor.</param>
/// <param name="TauSource"><c>estimated</c> or <c>floor</c>.</param>
/// <param name="Blend">B, 0 to 1: how far the shrunk mean has moved from the parent toward the cell's own mean.</param>
/// <param name="ShrunkMeanR">The shrunk mean R: an estimate of the past edge, pulled toward its group.</param>
/// <param name="ShrunkLowerR">The 5 % quantile of the posterior (the level setting); ignores the uncertainty in the pooling parameters.</param>
/// <param name="EstimatedStrength">The Beta strength used for <paramref name="ShrunkWinRate"/>.</param>
/// <param name="StrengthSource"><c>estimated</c> or <c>default</c>.</param>
/// <param name="ShrunkWinRate">The win rate shrunk toward the leave-one-out parent with the strength above (the P6-01 posterior with strength 20 stays on the row).</param>
/// <param name="NUsed">The number of trades the precision rests on: the smaller of effective n and the Kish weight n.</param>
/// <param name="Note">Remarks and the unavailable reason, joined by "; "; empty when there is nothing to say.</param>
public sealed record ShrinkageAssessment(
    ConfidenceMode Mode,
    string? ParentLevel,
    double? ParentMeanR,
    double? TauSquared,
    string? TauSource,
    double? Blend,
    double? ShrunkMeanR,
    double? ShrunkLowerR,
    double? EstimatedStrength,
    string? StrengthSource,
    double? ShrunkWinRate,
    double? NUsed,
    string Note)
{
    /// <summary>True when the shrunk mean was computed.</summary>
    public bool IsAvailable => ShrunkMeanR.HasValue;

    /// <summary>The assessment of a cell that cannot be shrunk: only the reason.</summary>
    public static ShrinkageAssessment Unavailable(ConfidenceMode mode, string reason, double? nUsed = null) =>
        new(mode, null, null, null, null, null, null, null, null, null, null, nUsed, "unavailable: " + reason);
}

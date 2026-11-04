using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The shrunk figures of a cell row (CF-02, ADR 0204), as the API shows them beside the raw ones: "an estimate of the past edge, pulled toward its group", not a forecast. Every figure is null when it could not be computed and <see cref="Note"/> then
/// says <c>unavailable: &lt;reason&gt;</c>. It changes no other figure of the row.
/// </summary>
/// <param name="Mode"><c>ReportOnly</c> (the only mode offered), the mode when it was computed.</param>
/// <param name="ParentLevel"><c>star</c>, <c>strategy</c> or <c>system</c>: the group the cell is pulled toward, itself excluded.</param>
/// <param name="ParentMeanR">The mean R of that group without the cell.</param>
/// <param name="TauSquared">The between-cell variance used, in R squared (never below the floor).</param>
/// <param name="TauSource"><c>estimated</c> or <c>floor</c>.</param>
/// <param name="Blend">How far, 0 to 1, from the parent toward the cell's own mean.</param>
/// <param name="ShrunkMeanR">The shrunk mean R.</param>
/// <param name="ShrunkLowerR">The 5 % quantile of the posterior: ignores the uncertainty in the pooling parameters, so it is optimistic.</param>
/// <param name="EstimatedStrength">The Beta strength the shrunk win rate used.</param>
/// <param name="StrengthSource"><c>estimated</c> or <c>default</c>.</param>
/// <param name="ShrunkWinRate">The win rate shrunk toward the group's with that strength (the P6-01 posterior stays on the row).</param>
/// <param name="NUsed">The trades the precision rests on: the smaller of effective n and the weight n.</param>
/// <param name="Note">Remarks and the unavailable reason.</param>
public sealed record ShrinkageView(
    string Mode,
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
    /// <summary>Printed with the figures (the spec's honest limits).</summary>
    public const string LimitsText = "Shrunk R is an estimate of the past edge pulled toward its group, not a forecast. Its lower bound ignores the uncertainty in the pooling parameters, so it is optimistic. The between-cell variance comes from a handful of siblings that share a market period and has a floor that is an assumption. The normal model for R is an approximation. The between-cell variance and the pooled variance include the cell; only the parent mean leaves it out.";

    internal static ShrinkageView? Of(ShrinkageAssessment? a) => a is null || a.Mode == ConfidenceMode.Off ? null : new ShrinkageView(
        a.Mode.ToString(), a.ParentLevel, a.ParentMeanR, a.TauSquared, a.TauSource, a.Blend, a.ShrunkMeanR, a.ShrunkLowerR, a.EstimatedStrength, a.StrengthSource, a.ShrunkWinRate, a.NUsed, a.Note);
}

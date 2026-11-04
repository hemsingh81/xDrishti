using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Confidence;

/// <summary>
/// How many independent trades a cell or a verdict rests on (CF-01, ADR 0203), as the API shows it. Every figure is null when it could not be computed and <see cref="Note"/> then says <c>unavailable: &lt;reason&gt;</c>.
/// <c>IndependentN</c> (here <see cref="NEff"/>) is trades after same-session clustering and serial correction: not <c>EffectiveN</c> of a cell row (the recency-weight sum). Shown beside the old figures; it changes nothing unless the mode is <c>Gate</c>.
/// </summary>
/// <param name="Mode"><c>Off</c>, <c>ReportOnly</c> or <c>Gate</c>, the mode when it was computed.</param>
/// <param name="N">Trades.</param>
/// <param name="NEff">Independent trades (1 to N).</param>
/// <param name="DesignEffect">N over NEff.</param>
/// <param name="Method"><c>cluster</c>, <c>serial</c> or <c>none</c>.</param>
/// <param name="NeededTrades">Independent trades needed to see the target edge (a lower bound: normal R).</param>
/// <param name="MinDetectableEdgeR">The smallest edge in R that NEff independent trades could see now.</param>
/// <param name="MinTrl">Minimum track record in iid trades.</param>
/// <param name="MinTrlDependent">MinTrl times the design effect: trades at this set's dependence.</param>
/// <param name="YearsToEvidence">Years to reach NeededTrades at the arrival rate of NEff, if the rate and the edge hold.</param>
/// <param name="EnoughByNEff">Whether effective n reaches the minimum trades; null when unavailable.</param>
/// <param name="Note">Remarks and unavailable reasons.</param>
/// <param name="Summary">One line of words for a screen.</param>
/// <param name="TargetEdgeR">The edge in R that <paramref name="NeededTrades"/> is about.</param>
/// <param name="Settings">The settings that produced the block (Lab verdicts); null for a stored cell row, whose snapshot key records them.</param>
public sealed record DependenceView(
    string Mode,
    int N,
    double? NEff,
    double? DesignEffect,
    string Method,
    int? NeededTrades,
    double? MinDetectableEdgeR,
    int? MinTrl,
    int? MinTrlDependent,
    double? YearsToEvidence,
    bool? EnoughByNEff,
    string Note,
    string Summary,
    double? TargetEdgeR = null,
    string? Settings = null)
{
    /// <summary>Printed with the figures (the spec's honest limits).</summary>
    public const string LimitsText = "Effective n is an estimate from the same thin sample it corrects; below 20 trades it is noisy. It captures same-session and serial dependence, not a regime shared across weeks. The entry-session cluster is coarse. Power assumes roughly normal R, so the needed number is a lower bound. A power statement says nothing about whether an edge exists. MinTRL is in iid trades.";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal static DependenceView? Of(DependenceAssessment? a) => a is null || a.Mode == ConfidenceMode.Off ? null : new DependenceView(
        a.Mode.ToString(), a.N, a.NEff, a.DesignEffect, a.Method, a.NeededTrades, a.MinDetectableEdgeR, a.MinTrl, a.MinTrlDependent, a.YearsToEvidence, a.EnoughByNEff, a.Note, SummaryOf(a), a.TargetEdgeR, a.Settings);

    private static string SummaryOf(DependenceAssessment a)
    {
        if (a.NEff is not { } nEff)
        {
            return $"n={a.N.ToString(Inv)}; effective n {(a.Note.Length > 0 ? a.Note : "unavailable")}";
        }

        var edge = a.TargetEdgeR.HasValue ? a.TargetEdgeR.Value.ToString("0.##", Inv) + " R" : "the target edge";
        var needed = a.NeededTrades.HasValue ? $"needs about {a.NeededTrades.Value.ToString(Inv)} to see {edge}" : "needed trades unavailable";
        var visible = a.MinDetectableEdgeR.HasValue ? $"smallest visible now {a.MinDetectableEdgeR.Value.ToString("0.00", Inv)} R" : "smallest visible edge unavailable";
        var years = a.NeededTrades.HasValue ? "; " + PowerAnalysis.YearsText(a.YearsToEvidence) : string.Empty;
        return $"n={a.N.ToString(Inv)}, effective {nEff.ToString("0.#", Inv)}; {needed}; {visible}{years}";
    }
}

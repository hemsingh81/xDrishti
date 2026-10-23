using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>The phase check (P6-18): the three exit criteria and the limits that are always printed with them.</summary>
/// <param name="Criteria">The criteria in order.</param>
/// <param name="Limits">The honest limits; never empty.</param>
public sealed record PhaseCheckReport(IReadOnlyList<PhaseCriterion> Criteria, IReadOnlyList<string> Limits)
{
    /// <summary>The report as text. Deterministic: the same facts give the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string> { "phase check P6 (exit criteria of the roadmap, ADR 0080; stored records only, no new statistics)" };
        foreach (var criterion in Criteria)
        {
            lines.Add(string.Create(c, $"{criterion.Number}. {Label(criterion.Verdict)}  {criterion.Name}"));
            lines.Add("   " + criterion.Detail);
        }

        lines.Add(string.Create(c, $"summary: {Criteria.Count(x => x.Verdict == PhaseVerdict.Met)} met, {Criteria.Count(x => x.Verdict == PhaseVerdict.NotMet)} not met, {Criteria.Count(x => x.Verdict == PhaseVerdict.NotEvaluable)} not evaluable"));
        lines.AddRange(Limits.Select(l => "limit: " + l));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The label of a verdict as printed.</summary>
    public static string Label(PhaseVerdict verdict) => verdict switch
    {
        PhaseVerdict.Met => "MET",
        PhaseVerdict.NotMet => "NOT MET",
        _ => "NOT EVALUABLE",
    };
}

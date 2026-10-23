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
            lines.Add(string.Create(c, $"{criterion.Number}. {Label(criterion)}  {criterion.Name}"));
            lines.Add("   " + criterion.Detail);
        }

        var caveats = Criteria.Where(x => x.Verdict == PhaseVerdict.Met && x.Caveat is not null).Select(x => string.Create(c, $"{x.Number}: {x.Caveat}")).ToList();
        var metNote = caveats.Count == 0 ? string.Empty : " (criterion " + string.Join("; criterion ", caveats) + ")";
        lines.Add(string.Create(c, $"summary: {Criteria.Count(x => x.Verdict == PhaseVerdict.Met)} met{metNote}, {Criteria.Count(x => x.Verdict == PhaseVerdict.NotMet)} not met, {Criteria.Count(x => x.Verdict == PhaseVerdict.NotEvaluable)} not evaluable"));
        lines.AddRange(Limits.Select(l => "limit: " + l));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The label of a criterion as printed: its verdict, with the caveat beside a MET that rests on something not verified here.</summary>
    public static string Label(PhaseCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        return criterion.Verdict == PhaseVerdict.Met && criterion.Caveat is not null ? $"{Label(criterion.Verdict)} ({criterion.Caveat})" : Label(criterion.Verdict);
    }

    /// <summary>The label of a verdict as printed.</summary>
    public static string Label(PhaseVerdict verdict) => verdict switch
    {
        PhaseVerdict.Met => "MET",
        PhaseVerdict.NotMet => "NOT MET",
        _ => "NOT EVALUABLE",
    };
}

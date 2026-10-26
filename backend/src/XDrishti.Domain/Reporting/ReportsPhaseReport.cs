using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Reporting;

/// <summary>The P8 phase check (spec P8-17): ten criteria of P8-00 with a verdict each, the answer on the default set, the timings and the limits that are always printed.</summary>
/// <param name="Criteria">Criteria 1 to 10, in order.</param>
/// <param name="Answer">The What-works answer on the default set.</param>
/// <param name="Timings">The timed reads.</param>
/// <param name="Runs">Stored runs.</param>
/// <param name="Trades">Trades in the default set.</param>
/// <param name="BasketsWithTrades">Baskets with trades.</param>
/// <param name="BuiltReports">The reports the check called.</param>
/// <param name="Limits">The honest limits; never empty.</param>
public sealed record ReportsPhaseReport(
    IReadOnlyList<PhaseCriterion> Criteria,
    ReportsPhaseAnswer Answer,
    IReadOnlyList<ReportsPhaseTiming> Timings,
    int Runs,
    int Trades,
    int BasketsWithTrades,
    IReadOnlyList<string> BuiltReports,
    IReadOnlyList<string> Limits)
{
    /// <summary>The criteria that count toward the exit.</summary>
    public static IReadOnlyList<int> Counted { get; } = [1, 2, 3, 4, 5, 7, 8];

    /// <summary>The headline (spec P8-17 criterion 12): MET only when every counted criterion is MET and none of the ten is NOT MET; malformed input is never MET.</summary>
    public string Headline() => HeadlineOf(Criteria);

    /// <summary>The headline of a list of criteria.</summary>
    /// <param name="criteria">The criteria.</param>
    public static string HeadlineOf(IReadOnlyList<PhaseCriterion> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var numbers = criteria.Select(c => c.Number).ToList();
        if (numbers.Distinct().Count() != numbers.Count || Counted.Any(n => !numbers.Contains(n)))
        {
            return "P8 exit: NOT EVALUABLE, malformed report";
        }

        if (criteria.Any(c => c.Verdict == PhaseVerdict.NotMet))
        {
            return "P8 exit: NOT MET";
        }

        var open = criteria.Count(c => Counted.Contains(c.Number) && c.Verdict != PhaseVerdict.Met);
        if (open > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"P8 exit: NOT EVALUABLE ({open} of {Counted.Count} need evidence)");
        }

        var caveats = criteria.Where(c => Counted.Contains(c.Number) && c.Caveat is not null).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Number}: {c.Caveat}")).ToList();
        return caveats.Count == 0 ? "P8 exit: MET" : "P8 exit: MET (criterion " + string.Join("; criterion ", caveats) + ")";
    }

    /// <summary>The report as text. Deterministic: the same facts give the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string> { "phase check P8 (exit criteria of P8-00, spec P8-17; stored records and local evidence only, no new statistics)" };
        lines.Add(string.Create(c, $"stored: {Runs} runs, {Trades} trades in the default set, {BasketsWithTrades} baskets with trades"));
        if (Runs == 0)
        {
            lines.Add("no stored run: criteria 1, 2 and 3 cannot be evaluated; the probe of criterion 4 and the route check of criterion 5 still run");
        }

        foreach (var criterion in Criteria)
        {
            lines.Add(string.Create(c, $"{criterion.Number}. {PhaseCheckReport.Label(criterion)}  {criterion.Name}"));
            lines.Add("   " + criterion.Detail);
        }

        lines.Add("answer on the default set (" + Answer.Kind + "): " + Answer.Sentence);
        foreach (var t in Timings)
        {
            lines.Add(string.Create(c, $"timing {t.Operation}{(t.AnswerPath ? " (answer path)" : string.Empty)}: cold {Ms(t.ColdMs)}, warm {Ms(t.WarmMs)}"));
        }

        lines.Add("built reports: " + string.Join(", ", BuiltReports));
        lines.Add(HeadlineOf(Criteria));
        lines.AddRange(Limits.Select(l => "limit: " + l));
        return string.Join('\n', lines) + "\n";
    }

    private static string Ms(long? ms) => ms is null ? "not read" : ms.Value.ToString(CultureInfo.InvariantCulture) + " ms";
}

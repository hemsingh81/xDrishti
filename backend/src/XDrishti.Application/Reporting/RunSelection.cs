using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Which stored runs a report is computed over when the caller names none (P8-00 D2): for each strategy and universe the newest run; and, so that gross and net results are never mixed, only the runs costed the same way as the newest run of all. The
/// rest are listed with the reason. A caller who wants anything else names the runs.
/// </summary>
internal static class RunSelection
{
    /// <summary>The rule, as text for the runs list.</summary>
    public const string RuleText = "For each strategy and universe the newest run; and only runs costed the same way as the newest run of all, so gross and net results are never mixed. Name the runs to choose differently.";

    /// <summary>The default set and, for every run it leaves out, why.</summary>
    /// <param name="runs">Every stored run.</param>
    public static (IReadOnlyList<ReportRunInfo> Chosen, IReadOnlyDictionary<string, string> Notes) Default(IReadOnlyList<ReportRunInfo> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (runs.Count == 0)
        {
            return ([], notes);
        }

        var newest = runs.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.RunKey, StringComparer.Ordinal).First();
        var chosen = new List<ReportRunInfo>();
        foreach (var group in runs.GroupBy(r => (r.StrategyId, r.Universe)).OrderBy(g => g.Key.StrategyId, StringComparer.Ordinal).ThenBy(g => g.Key.Universe, StringComparer.Ordinal))
        {
            var members = group.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.RunKey, StringComparer.Ordinal).ToList();
            var take = members.FirstOrDefault(r => r.Costed == newest.Costed);
            foreach (var run in members)
            {
                if (run == take)
                {
                    chosen.Add(run);
                }
                else if (run.Costed != newest.Costed)
                {
                    notes[run.RunKey] = newest.Costed
                        ? "gross run: the newest run is costed and a report never mixes gross and net runs"
                        : "costed run: the newest run is gross and a report never mixes gross and net runs";
                }
                else
                {
                    notes[run.RunKey] = "an older run of the same strategy and universe";
                }
            }
        }

        return (chosen.AsReadOnly(), notes);
    }
}

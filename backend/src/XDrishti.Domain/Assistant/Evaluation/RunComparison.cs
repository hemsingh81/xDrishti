namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Compares two runs set by set over the cases they share (spec P9-09 AC 12). A case passed in a run when all its repeats passed. <c>regression</c> is flagged when a threshold now fails, or <c>b - c</c> is 2 or more on data / 1 or more on plans and ideas. Pure.
/// </summary>
public static class RunComparison
{
    public static IReadOnlyList<SetComparison> Compare(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> baseline, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> now, RunSummary nowSummary)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(nowSummary);
        var list = new List<SetComparison>();
        foreach (var set in EvalVerdict.Gating)
        {
            if (!baseline.TryGetValue(set, out var before) || !now.TryGetValue(set, out var after))
            {
                continue;
            }

            var shared = before.Keys.Intersect(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var paired = PairedComparison.Compare([.. shared.Select(c => before[c])], [.. shared.Select(c => after[c])]).Value;
            var limit = set == "data" ? 2 : 1;
            var failing = nowSummary.Find(set)?.Outcome.State == SetState.Fail;
            var regression = failing || paired.B - paired.C >= limit;
            list.Add(new SetComparison(set, shared.Count, shared.Count(c => after[c]), shared.Count(c => before[c]), paired, regression, PairedComparison.NeededSentence()));
        }

        return list;
    }
}

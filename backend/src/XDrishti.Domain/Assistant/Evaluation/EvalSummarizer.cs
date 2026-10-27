namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Turns the graded repeats of a run into the sets' summaries and the verdict (spec P9-09 AC 8 and 9). A case passes only if every one of its repeats passes (<c>pass^k</c>); <c>pass@1</c> and <c>stability</c> are reported beside it. A set with fewer cases than
/// the set holds is not run, an honest-state case with an ungrounded figure fails the whole data set. Pure.
/// </summary>
public static class EvalSummarizer
{
    public static RunSummary Build(IReadOnlyList<CaseRepeatResult> rows, ThresholdTable thresholds, int repeats, AnnouncementResult? events)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(thresholds);
        var sets = new List<SetSummary>();
        foreach (var set in new[] { "data", "plans", "ideas" })
        {
            var setRows = rows.Where(r => r.Set == set).ToList();
            if (setRows.Count > 0)
            {
                sets.Add(set switch { "data" => Data(setRows, thresholds, repeats), "plans" => Plans(setRows, thresholds, repeats), _ => Ideas(setRows, thresholds, repeats) });
            }
        }

        if (events is not null)
        {
            sets.Add(new SetSummary("events", new SetOutcome("events", events.State, events.Note is null ? [] : [events.Note]), Events(events), events.Rows, 1, null, null, null));
        }

        var outcomes = sets.Select(s => s.Outcome).ToList();
        return new RunSummary(EvalVerdict.Of(outcomes), repeats < 2, sets, thresholds.SmallSampleLine());
    }

    private static List<IGrouping<string, CaseRepeatResult>> ByCase(List<CaseRepeatResult> rows) => [.. rows.GroupBy(r => r.CaseId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)];

    private static bool All(IGrouping<string, CaseRepeatResult> g, int repeats, Func<CaseRepeatResult, bool> test) => g.Count() == repeats && g.All(test);

    private static SetSummary Data(List<CaseRepeatResult> rows, ThresholdTable t, int repeats)
    {
        var cases = ByCase(rows);
        var passes = cases.Count(g => All(g, repeats, r => r.Passed));
        var atOne = cases.Count(g => g.Any(r => r.Repeat == 1 && r.Passed));
        var stability = repeats > 1 ? (double)cases.Count(g => g.Select(r => r.AnswerKey).Distinct(StringComparer.Ordinal).Count() == 1 && g.Count() == repeats) / cases.Count : (double?)null;
        var outcome = t.EvaluateData(passes, cases.Count);
        if (rows.Any(r => r.Flag("hard_fail")))
        {
            outcome = SetOutcome.Of("data", SetState.Fail, [.. outcome.Reasons, "an honest-state question has an ungrounded figure"]);
        }

        return new SetSummary("data", outcome, [RateLine.Of("pass", passes, cases.Count), RateLine.Of("pass_at_1", atOne, cases.Count)], cases.Count, repeats, stability, null, null);
    }

    private static SetSummary Plans(List<CaseRepeatResult> rows, ThresholdTable t, int repeats)
    {
        var cases = ByCase(rows);
        var raw = cases.Count(g => All(g, repeats, r => r.Flag("raw")));
        var repaired = cases.Count(g => All(g, repeats, r => r.Flag("repaired")));
        var escapes = cases.Count(g => g.Any(r => r.Flag("escape")));
        var covered = cases.Count(g => All(g, repeats, r => r.Flag("covered")));
        var outcome = t.EvaluatePlans(new PlanCounts(cases.Count, raw, repaired, escapes, covered));
        return new SetSummary("plans", outcome, [RateLine.Of("raw_pass", raw, cases.Count), RateLine.Of("repaired_pass", repaired, cases.Count), RateLine.Of("covered", covered, cases.Count), RateLine.Of("escape", escapes, cases.Count)], cases.Count, repeats, null, null, null);
    }

    private static SetSummary Ideas(List<CaseRepeatResult> rows, ThresholdTable t, int repeats)
    {
        var cases = ByCase(rows);
        var first = cases.Count(g => All(g, repeats, r => r.Flag("first_shot")));
        var valid = cases.Count(g => All(g, repeats, r => r.Flag("valid")));
        var intents = cases.Select(g => g.Average(r => r.Score ?? 0)).ToList();
        var declines = cases.Where(g => g.Any(r => r.Flag("decline_applicable"))).ToList();
        var declineOk = declines.Count(g => All(g, repeats, r => r.Flag("decline_correct")));
        var mean = intents.Count == 0 ? 0 : intents.Average();
        var lowest = intents.Count == 0 ? 0 : intents.Min();
        var outcome = t.EvaluateIdeas(new IdeaCounts(cases.Count, first, valid, mean, lowest, declineOk, declines.Count));
        return new SetSummary("ideas", outcome, [RateLine.Of("first_shot_valid", first, cases.Count), RateLine.Of("repaired_valid", valid, cases.Count), RateLine.Of("decline_correct", declineOk, declines.Count)], cases.Count, repeats, null, mean, lowest);
    }

    private static List<RateLine> Events(AnnouncementResult e)
    {
        var lines = new List<RateLine>();
        if (e.TruePositive + e.FalsePositive > 0)
        {
            lines.Add(RateLine.Of("precision_material", e.TruePositive, e.TruePositive + e.FalsePositive));
        }

        if (e.TruePositive + e.FalseNegative > 0)
        {
            lines.Add(RateLine.Of("recall_material", e.TruePositive, e.TruePositive + e.FalseNegative));
        }

        return lines;
    }
}

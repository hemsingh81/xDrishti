using System.Globalization;
using System.Text;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The weekly learning digest (P7-15, ADR 0094): a pure function from stored records to sections and text. It only lists and counts; it computes no statistic, reads no clock and applies nothing. The same input gives the same text,
/// byte for byte. Every section ends with <see cref="SectionNote"/> and the text opens with <see cref="NothingApplied"/>; both are pinned by tests and by a check on the stored row.
/// </summary>
public static class DigestBuilder
{
    /// <summary>The sentence the digest opens with, and the phrase the stored row must contain.</summary>
    public const string NothingApplied = "Nothing was applied: every item below is a proposal; applying any of it is the owner's own act.";

    /// <summary>The closing line of every section.</summary>
    public const string SectionNote = "Nothing was applied in this section: these are stored records and proposals for the owner.";

    /// <summary>Printed in a section with nothing to list.</summary>
    public const string None = "- none";

    /// <summary>Builds the digest.</summary>
    public static WeeklyDigest Build(DigestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sections = new List<DigestSection>
        {
            Section("cells", "New and changed cells", Cells(input)),
            Section("drift", "Drift and calibration proposals", Items(input.DriftProposals, "open or linked lessons from the drift and calibration monitors", input)),
            Section("lessons", "Lessons", Lessons(input)),
            Section("hypotheses", "Hypotheses and experiments awaiting the owner", Hypotheses(input)),
            Section("loss_tags", "Loss-tag trends", Items(input.LossTags, "open or linked lessons from the growing loss-tag monitor", input)),
            Section("plans", "Plan outcomes (simulated on plan prices)", Plans(input.Plans)),
            Section("models", "Model registry and the phase check", Models(input)),
            Section("limits", "Data quality and honest limits", Limits(input)),
        };
        var week = input.Week;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Weekly learning digest {week.Key} ({week.Start:yyyy-MM-dd} to {week.End:yyyy-MM-dd})\n{NothingApplied}\n");
        var number = 1;
        foreach (var s in sections)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{number++}. {s.Title}\n");
            foreach (var line in s.Lines)
            {
                text.Append(line).Append('\n');
            }
        }

        return new WeeklyDigest(week, sections, text.ToString());
    }

    private static DigestSection Section(string key, string title, IEnumerable<string> lines) => new(key, title, [.. lines, SectionNote]);

    /// <summary>The line of one item: <c>- #12 kind subject [state] text</c>.</summary>
    public static string Line(DigestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var subject = item.Subject.Length == 0 ? string.Empty : " " + item.Subject;
        return string.Create(CultureInfo.InvariantCulture, $"- #{item.Id} {item.Kind}{subject} [{item.State}] {item.Text}");
    }

    private static IEnumerable<string> Items(IReadOnlyList<DigestItem> items, string what, DigestInput input)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"{items.Count} {what}.");
        if (items.Count == 0)
        {
            yield return None;
        }

        foreach (var item in items)
        {
            yield return Line(item);
        }

        foreach (var t in Truncation(input, "lessons"))
        {
            yield return t;
        }
    }

    private static IEnumerable<string> Truncation(DigestInput input, string kind)
    {
        if (input.Truncated.Contains(kind, StringComparer.Ordinal))
        {
            yield return $"Note: the newest 200 {kind} were read; there may be more.";
        }
    }

    private static IEnumerable<string> Cells(DigestInput input)
    {
        yield return input.CellsAsOf is { } asOf
            ? string.Create(CultureInfo.InvariantCulture, $"Newest cell-statistics snapshot: as of {asOf:yyyy-MM-dd}.")
            : "No cell-statistics snapshot is stored yet.";
        yield return string.Create(CultureInfo.InvariantCulture, $"{input.CellChanges.Count} cell state change(s) recorded this week (changelog).");
        if (input.CellChanges.Count == 0)
        {
            yield return None;
        }

        foreach (var item in input.CellChanges)
        {
            yield return Line(item);
        }

        foreach (var t in Truncation(input, "changelog"))
        {
            yield return t;
        }
    }

    private static IEnumerable<string> Lessons(DigestInput input)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"Open lessons: {input.OpenLessonCount}; linked to a fix: {input.LinkedLessonCount} (all monitors; the drift and loss-tag ones are listed in their own sections).");
        yield return string.Create(CultureInfo.InvariantCulture, $"{input.LessonsResolved.Count} lesson(s) closed or dismissed this week.");
        if (input.LessonsResolved.Count == 0)
        {
            yield return None;
        }

        foreach (var item in input.LessonsResolved)
        {
            yield return Line(item);
        }

        foreach (var t in Truncation(input, "lessons"))
        {
            yield return t;
        }
    }

    private static IEnumerable<string> Hypotheses(DigestInput input)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"{input.HypothesesAwaiting.Count} hypothesis(es) proposed or judged, waiting for the owner; {input.HypothesesInProgress} approved or running.");
        if (input.HypothesesAwaiting.Count == 0)
        {
            yield return None;
        }

        foreach (var item in input.HypothesesAwaiting)
        {
            yield return Line(item);
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"{input.Experiments.Count} experiment(s) recorded this week.");
        if (input.Experiments.Count == 0)
        {
            yield return None;
        }

        foreach (var item in input.Experiments)
        {
            yield return Line(item);
        }

        foreach (var t in Truncation(input, "hypotheses").Concat(Truncation(input, "experiments")))
        {
            yield return t;
        }
    }

    private static IEnumerable<string> Plans(GradeReportData? plans)
    {
        var c = CultureInfo.InvariantCulture;
        if (plans is null || plans.Plans == 0)
        {
            yield return "No graded plan for the sessions of this week.";
            yield return "Plan outcomes are simulated on plan prices, not on fills.";
            yield break;
        }

        yield return string.Create(c, $"{plans.Plans} graded plan day(s); basis: {plans.Basis}.");
        foreach (var curve in plans.Curves.Where(x => x.Name == "top-all"))
        {
            yield return "- " + Stats(curve.Totals, "all primary and reserve tickets");
        }

        foreach (var group in plans.Groups)
        {
            yield return "- " + Stats(group, group.Name);
        }

        foreach (var note in plans.Notes)
        {
            yield return "Note: " + note;
        }
    }

    private static string Stats(GradeStats s, string label) =>
        string.Create(CultureInfo.InvariantCulture, $"{label}: {s.Tickets} tickets, {s.Triggered} triggered, {s.NotTriggered} not triggered, {s.NotGradable} not gradable, total {s.TotalR:F2} R{(s.Note is null ? string.Empty : " (" + s.Note + ")")}");

    private static IEnumerable<string> Models(DigestInput input)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"{input.Models.Count} model(s) as champion or challenger.");
        if (input.Models.Count == 0)
        {
            yield return None;
        }

        foreach (var item in input.Models)
        {
            yield return Line(item);
        }

        foreach (var t in Truncation(input, "models"))
        {
            yield return t;
        }

        if (input.PhaseCriteria is null)
        {
            yield return "Phase check unavailable: " + (input.PhaseProblem ?? "not read") + ".";
            yield break;
        }

        foreach (var cr in input.PhaseCriteria)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"Phase criterion {cr.Number}: {PhaseCheckReport.Label(cr)} - {cr.Name}");
            yield return cr.Number == 3
                ? "  detail: the timing is measured on each run; see xd learn phase-check."
                : "  detail: " + cr.Detail;
        }
    }

    private static IEnumerable<string> Limits(DigestInput input)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"Everything here rests on about 2.1 years of data and {PhaseCheck.UsableFoldsText} usable validation folds; fills are modelled, and plan outcomes are simulated on plan prices, not on fills.");
        yield return "Lists read the newest 200 records of each kind; a note says where there may be more.";
        yield return "Statistics are as stored when each record was made; this digest recomputes nothing and is stored once per ISO week.";
        foreach (var limit in input.PhaseLimits)
        {
            yield return "Phase check limit: " + limit;
        }
    }
}

using System.Globalization;
using XDrishti.Domain.Execution.PhaseCheck;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The phase check of P15 (spec P15-12): "features and filters improve OOS or are dropped", read from the stored verdicts of the five families and nothing else. A pure function. Per family the newest record <b>for the current versions</b> counts; a record of an older
/// version is stale and the family is open. MET needs every non-cut family resolved (KEEP or DROP, with no missing quantity and rows behind it) and no violation; NOT MET needs a violation (the event filter ON without a current KEEP: reported, never blocked); anything
/// else is NOT EVALUABLE with each open family and its missing quantity named. Nothing missing, thin, stale or unreadable is ever MET.
/// </summary>
public static class EventsPhaseCheck
{
    /// <summary>Seasons of bars protocol P needs.</summary>
    public const int SeasonsNeeded = 20;

    public const string StatusCut = "cut";

    public const string StatusNone = "none";

    public const string StatusStale = "stale";

    public const string StatusUnreadable = "unreadable";

    /// <summary>The limits that make a verdict weak; printed in every report.</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "Only stored verdicts are read; no evaluation is run and nothing is written, no setting changed and no order placed.",
        "R is gross unless the owner's checked charge sheet exists (ISS-25).",
        "Survivorship (ISS-22): the instrument lists hold today's names, so every verdict is read with that bias.",
        "A verdict for an older rule, catalog, parser, engine or lag version is stale and counts for nothing.",
        "A KEEP of the event filter is a record; the hold-out column says whether the reserved seasons were read.",
        "A violation (the filter ON without a current KEEP) is reported; the owner may keep it, nothing here switches it.",
    ];

    /// <summary>Judges the facts; <paramref name="cut"/> are the families the owner leaves out of the overall (they are listed, never counted).</summary>
    public static EventsPhaseReport Evaluate(EventsPhaseFacts facts, IReadOnlyCollection<string> cut)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(cut);
        var cutSet = ExtensionFamilies.All.Where(f => cut.Contains(f, StringComparer.Ordinal)).ToList();
        var lines = new List<EventsFamilyLine>();
        foreach (var family in ExtensionFamilies.All)
        {
            var given = facts.Families.FirstOrDefault(f => f.Family == family);
            lines.Add(cutSet.Contains(family) ? CutLine(family) : Status(family, given));
        }

        var violations = new List<EventsViolation>();
        // A violation is a fact about the live switch: it is judged even when the owner cut the family from the overall.
        var filterStatus = Status(ExtensionFamilies.Filter, facts.Families.FirstOrDefault(f => f.Family == ExtensionFamilies.Filter));
        // An unreadable verdict store is not a finding against the owner: the family reads open ("verdict unreadable") and the check cannot be MET (ADR 0190).
        if (facts.Filter.On && filterStatus.Status != StatusUnreadable && !(filterStatus.Status == "KEEP" && filterStatus.Resolved))
        {
            var by = facts.Filter.DecidedBy ?? "unavailable";
            var row = facts.Filter.DecisionId is { } id ? id.ToString(CultureInfo.InvariantCulture) : "unavailable";
            violations.Add(new EventsViolation(
                ExtensionFamilies.Filter,
                $"the event filter is ON (N={facts.Filter.Days.ToString(CultureInfo.InvariantCulture)}, decision row {row}, decided by {by}) without a current KEEP (newest verdict: {filterStatus.Status})",
                facts.Filter.DecisionId,
                facts.Filter.DecidedBy));
        }

        var counted = lines.Where(l => !l.Cut).ToList();
        var open = counted.Where(l => !l.Resolved).Select(l => $"{l.Family} ({l.Status}): {l.Detail}").ToList();
        var anyRecord = facts.Families.Any(f => f.Records.Count > 0);
        if (!facts.Filter.Readable)
        {
            // Whether the filter is ON cannot be told, so a violation cannot be ruled out: never MET.
            open.Add("event filter switch (unreadable): the decision history could not be read, so a family in use without a KEEP cannot be ruled out");
        }

        string verdict;
        string summary;
        if (violations.Count > 0)
        {
            verdict = PhaseVerdicts.NotMet;
            summary = $"{violations.Count.ToString(CultureInfo.InvariantCulture)} violation(s): a family is in use without a current KEEP.";
        }
        else if (counted.Count == 0)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "every family is cut: nothing is judged.";
        }
        else if (!anyRecord)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "no P15 evaluation stored.";
        }
        else if (open.Count > 0)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = $"{open.Count.ToString(CultureInfo.InvariantCulture)} of {counted.Count.ToString(CultureInfo.InvariantCulture)} families are open.";
        }
        else
        {
            verdict = PhaseVerdicts.Met;
            summary = $"all {counted.Count.ToString(CultureInfo.InvariantCulture)} counted families are resolved (KEEP or DROP) for the current versions and no family is in use without a KEEP.";
        }

        return new EventsPhaseReport(
            verdict,
            summary,
            lines,
            open,
            violations,
            facts.SeasonsHeld,
            SeasonsNeeded,
            facts.Filings,
            facts.TrackedInstruments,
            facts.InstrumentsWithEntry,
            facts.NoUpcomingShare is { } s && double.IsFinite(s) ? s : null,
            facts.NoUpcomingRecord,
            facts.Filter.On,
            cutSet,
            Limits);
    }

    private static EventsFamilyLine CutLine(string family) => new(family, StatusCut, false, true, null, null, "unavailable", "cut by the owner: listed, not counted");

    private static EventsFamilyLine Status(string family, EventsFamilyFacts? facts)
    {
        if (facts is null || !facts.Readable)
        {
            return new EventsFamilyLine(family, StatusUnreadable, false, false, null, null, "unavailable", "verdict unreadable: the stored evaluations could not be read");
        }

        if (facts.Records.Count == 0)
        {
            return new EventsFamilyLine(family, StatusNone, false, false, null, null, "unavailable", "no P15 evaluation stored for this family");
        }

        var current = facts.Records.Where(r => Stale(r, facts.Current) is null).ToList();
        var holdOut = current.Any(r => r.HoldOutRead) ? "read" : "not read";
        if (current.Count == 0)
        {
            var newest = facts.Records[0];
            return new EventsFamilyLine(family, StatusStale, false, false, newest.Id, newest.Trials, "unavailable", $"newest record is for another version ({Stale(newest, facts.Current)}); it was {newest.Verdict}{MissingText(newest)}");
        }

        var record = family == ExtensionFamilies.Flag ? FlagRecord(current) : current[0];
        if (record is null)
        {
            var grade = current.FirstOrDefault(r => r.Protocol == "L");
            return new EventsFamilyLine(family, "NOT_EVALUABLE", false, false, grade?.Id, grade?.Trials, holdOut, $"the comparison (protocol X) is not stored{(grade is null ? string.Empty : "; the grade (protocol L) is " + grade.Verdict + " and alone resolves nothing")}");
        }

        var word = record.Verdict;
        if (word is "KEEP" or "DROP")
        {
            if (record.Missing.Count > 0 || record.Rows <= 0)
            {
                var why = record.Missing.Count > 0 ? "named missing quantities" : "no rows behind it";
                return new EventsFamilyLine(family, word, false, false, record.Id, record.Trials, holdOut, $"thin evidence: {word} with {why}{MissingText(record)}");
            }

            return new EventsFamilyLine(family, word, true, false, record.Id, record.Trials, holdOut, $"rows {record.Rows.ToString(CultureInfo.InvariantCulture)}");
        }

        var detail = word is "INCONCLUSIVE" or "NOT_EVALUABLE"
            ? (record.Missing.Count > 0 ? "missing: " + string.Join("; ", record.Missing) : "no missing quantity named")
            : "unrecognised verdict word";
        return new EventsFamilyLine(family, word is "INCONCLUSIVE" or "NOT_EVALUABLE" ? word : "NOT_EVALUABLE", false, false, record.Id, record.Trials, holdOut, detail);
    }

    // The flag family has two records, protocol L (the set D grade) and protocol X (the feature comparison). A failed grade is the drop; a passed grade is not yet a use of the feature, so the comparison decides.
    private static EventsRecordFact? FlagRecord(List<EventsRecordFact> current)
    {
        var grade = current.FirstOrDefault(r => r.Protocol == "L");
        var comparison = current.FirstOrDefault(r => r.Protocol == "X");
        return grade is { Verdict: "DROP" } ? grade : comparison;
    }

    private static string MissingText(EventsRecordFact record) => record.Missing.Count > 0 ? "; missing: " + string.Join("; ", record.Missing) : string.Empty;

    // Null when the record is for the current versions; else the first difference in words.
    private static string? Stale(EventsRecordFact record, IReadOnlyDictionary<string, string> current)
    {
        foreach (var (key, now) in current.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!record.Versions.TryGetValue(key, out var recorded))
            {
                return $"{key} not recorded, current {now}";
            }

            if (!string.Equals(recorded, now, StringComparison.Ordinal))
            {
                return $"{key} {recorded}, current {now}";
            }
        }

        return null;
    }
}

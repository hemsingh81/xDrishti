using System.Globalization;
using XDrishti.Domain.Execution.PhaseCheck;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The phase check of P14 (spec P14-17): "an option expression beats cash/futures out of sample after costs, else it stays disabled", read from the stored verdicts only. A pure function over plain facts. Per family the newest record <b>for the current versions</b> counts (an older
/// one is stale and the family is open). A family is resolved by a DROP with its evidence on the net basis the protocol requires, or by a KEEP that a hold-out record confirms; a development KEEP without a hold-out record is a candidate, not evidence. MET needs every non-cut family resolved,
/// no violation, and either an expression family KEEP or every counted expression family DROP ("stays disabled"). NOT MET needs a violation: a stored plan item or paper entry with a non-cash eighth cell-key part. Everything else is NOT EVALUABLE, naming each open family and its missing
/// quantity. Nothing missing, gross, stale, thin or unreadable is ever MET.
/// </summary>
public static class OptionsPhaseCheck
{
    /// <summary>The basis family of P14-14/15; it does not exist yet, so it reads <c>none stored</c> unless the owner cut it.</summary>
    public const string BasisCarry = "basis:carry-1";

    /// <summary>Years of underlying bars the expression study needs (CF-04).</summary>
    public const double YearsNeeded = 5;

    /// <summary>Usable chain sessions per underlying the half-spread model needs (P14-07).</summary>
    public const int SessionsNeeded = 60;

    /// <summary>The most violating rows the report lists.</summary>
    public const int MaxViolationRows = 20;

    public const string StatusCut = "cut";

    public const string StatusNone = "none";

    public const string StatusStale = "stale";

    public const string StatusUnreadable = "unreadable";

    private const string ExpressionPrefix = "expr:";

    /// <summary>The families the check judges, in the order the verb lists them. Built from constants, so this type names no family list.</summary>
    public static IReadOnlyList<string> Families { get; } =
    [
        ExpressionFamilies.OptionBuy, ExpressionFamilies.DebitSpread, ExpressionFamilies.CreditSpread, ExpressionFamilies.IronCondor, IvFeatureCatalog.Family, PairFamilies.Intraday, BasisCarry,
    ];

    /// <summary>The limits that make a verdict weak; printed in every report.</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "Only stored verdicts are read; no evaluation is run and nothing is written, no setting or switch changed and no order placed.",
        "A verdict is read on the net basis the protocol requires; a gross record can only be INCONCLUSIVE (a gross DROP of the pairs study is valid) and leaves its family open.",
        "A KEEP closes a family only when the one reading of the hold-out confirmed it; a development KEEP is a candidate.",
        "A record for an older re-mark version, engine, archive watermark, spread model, cost sheet or profile is stale and counts for nothing.",
        "The expiry weekdays are derived, not stored, until the contract master confirms them (UNVERIFIED); the derived share is printed per family.",
        "Survivorship (ISS-22): the instrument lists hold today's names, so every verdict is read with that bias.",
        "No decision path reads an option verdict: a KEEP is a research record and the expression stays cash everywhere.",
        "Expected answer for months: NOT EVALUABLE (not enough years of bars, not enough usable chain sessions, no charge lines for the net basis).",
    ];

    /// <summary>Years between the first and the last bar (365.25 days a year), or null when either is unknown.</summary>
    public static double? YearsBetween(DateTimeOffset? first, DateTimeOffset? last) =>
        first is { } f && last is { } l && l >= f ? (l - f).TotalDays / 365.25 : null;

    /// <summary>Judges the facts; <paramref name="cut"/> are the families the owner leaves out of the overall (they are listed, never counted).</summary>
    public static OptionsPhaseReport Evaluate(OptionsPhaseFacts facts, IReadOnlyCollection<string> cut)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(cut);
        var cutSet = Families.Where(f => cut.Contains(f, StringComparer.Ordinal)).ToList();
        var lines = new List<OptionsFamilyLine>();
        foreach (var family in Families)
        {
            lines.Add(cutSet.Contains(family) ? CutLine(family) : Line(family, facts.Families.FirstOrDefault(f => f.Family == family), facts.LatestFetchEndedAt, facts.StaleAfterDays));
        }

        var violations = facts.Cells.Readable
            ? facts.Cells.Rows.Take(MaxViolationRows).Select(r => new OptionsViolation(r.Source, r.Id, "the cell key has a non-cash eighth part: a non-cash expression is in use (the decision paths read cash only)")).ToList()
            : [];
        var counted = lines.Where(l => !l.Cut).ToList();
        var open = counted.Where(l => !l.Resolved).Select(l => $"{l.Family} ({l.Status}): {l.Detail}").ToList();
        if (!facts.Cells.Readable)
        {
            open.Add("stored plan items and paper entries (unreadable): they could not be read, so a non-cash expression in use cannot be ruled out");
        }

        var expressions = counted.Where(l => l.Family.StartsWith(ExpressionPrefix, StringComparison.Ordinal)).ToList();
        var anyKeep = expressions.Any(l => l.Resolved && l.Status == "KEEP");
        var allDrop = expressions.Count > 0 && expressions.All(l => l.Resolved && l.Status == "DROP");
        var anyRecord = facts.Families.Any(f => f.Records.Count > 0);

        string verdict;
        string summary;
        if (violations.Count > 0)
        {
            verdict = PhaseVerdicts.NotMet;
            summary = $"{violations.Count.ToString(CultureInfo.InvariantCulture)} violation(s): a non-cash expression is in use.";
        }
        else if (counted.Count == 0)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "every family is cut: nothing is judged.";
        }
        else if (!anyRecord)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "no P14 evaluation stored.";
        }
        else if (open.Count > 0)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = $"{open.Count.ToString(CultureInfo.InvariantCulture)} of {counted.Count.ToString(CultureInfo.InvariantCulture)} counted items are open.";
        }
        else if (expressions.Count == 0)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "every expression family is cut: the criterion is not judged.";
        }
        else if (anyKeep)
        {
            verdict = PhaseVerdicts.Met;
            summary = "an option expression is KEEP on the hold-out after costs and every counted family is resolved; the expression still stays cash on every decision path until the owner decides.";
        }
        else if (allDrop)
        {
            verdict = PhaseVerdicts.Met;
            summary = "option expressions stay disabled: every counted expression family is DROP, every counted family is resolved and the expression stays cash.";
        }
        else
        {
            verdict = PhaseVerdicts.NotEvaluable;
            summary = "no expression family is resolved.";
        }

        return new OptionsPhaseReport(
            verdict,
            summary,
            lines,
            open,
            violations,
            facts.Cells.Readable,
            facts.Cells.Truncated || facts.Cells.Rows.Count > MaxViolationRows,
            facts.Archive.YearsHeld is { } y && double.IsFinite(y) ? y : null,
            YearsNeeded,
            facts.Archive.Underlyings,
            SessionsNeeded,
            facts.Archive.RecorderOn,
            facts.Archive.BackfillOn,
            allDrop ? ArchiveNote(facts.Archive) : null,
            cutSet,
            facts.StaleAfterDays,
            Limits);
    }

    private static string ArchiveNote(OptionsArchiveFacts archive) =>
        archive.RecorderOn == true || archive.BackfillOn == true
            ? "archive still recording after DROP: owner may switch it off (information, not a violation)"
            : archive.RecorderOn == false && archive.BackfillOn == false
                ? "archive is not recording (both switches OFF)"
                : "archive switches unavailable";

    private static OptionsFamilyLine CutLine(string family) => new(family, StatusCut, false, true, null, null, "unavailable", "unavailable", null, null, "cut by the owner: listed, not counted");

    private static OptionsFamilyLine Line(string family, OptionsFamilyFacts? facts, DateTimeOffset? latestFetch, int staleAfterDays)
    {
        if (facts is null || !facts.Readable)
        {
            return new OptionsFamilyLine(family, StatusUnreadable, false, false, null, null, "unavailable", "unavailable", null, null, "verdict unreadable: the stored evaluations could not be read");
        }

        if (facts.Records.Count == 0)
        {
            return new OptionsFamilyLine(family, StatusNone, false, false, null, null, "unavailable", "unavailable", null, null, family == BasisCarry ? "none stored: the basis family is not built" : "no P14 evaluation stored for this family");
        }

        var current = facts.Records.Where(r => Stale(r, latestFetch, staleAfterDays) is null).ToList();
        if (current.Count == 0)
        {
            var newest = facts.Records[0];
            return new OptionsFamilyLine(family, StatusStale, false, false, newest.Id, newest.Trials, newest.Basis, "unavailable", newest.DerivedExpiryShare, newest.FallbackShare, $"newest record is for another version ({Stale(newest, latestFetch, staleAfterDays)}); it was {newest.Verdict}{MissingText(newest)}");
        }

        var holdOut = current.Any(r => r.HoldOutRead) ? "read" : "not read";
        var development = current.FirstOrDefault(r => !r.HoldOutRead);
        if (development is null)
        {
            var only = current[0];
            return Open(family, only, "NOT_EVALUABLE", holdOut, "only a hold-out record is stored for the current versions; the development record is missing or stale");
        }

        // A hold-out record confirms only the development record it was read for (its sources name it).
        var confirming = current.FirstOrDefault(r => r.HoldOutRead && r.DevelopmentRecord == development.Id);
        var otherHoldOut = current.FirstOrDefault(r => r.HoldOutRead);
        switch (development.Verdict)
        {
            case "DROP":
                return Sound(development, "DROP") is { } dropFlaw
                    ? Open(family, development, "DROP", holdOut, dropFlaw)
                    : Line(family, development, "DROP", true, holdOut, $"rows {development.Rows.ToString(CultureInfo.InvariantCulture)}");
            case "KEEP":
                if (Sound(development, "KEEP") is { } keepFlaw)
                {
                    return Open(family, development, "KEEP", holdOut, keepFlaw);
                }

                if (confirming is null)
                {
                    return otherHoldOut is null
                        ? Open(family, development, "KEEP", holdOut, "hold-out not read: a development KEEP is a candidate, not evidence")
                        : Open(family, development, "KEEP", holdOut, $"hold-out read for record {otherHoldOut.DevelopmentRecord ?? "unavailable"}, not for the newest development record {development.Id}");
                }

                if (confirming.Verdict == "KEEP" && Sound(confirming, "KEEP") is null)
                {
                    return Line(family, development, "KEEP", true, holdOut, $"confirmed on the hold-out (record {confirming.Id}); rows {development.Rows.ToString(CultureInfo.InvariantCulture)}");
                }

                return Open(family, development, "KEEP", holdOut, $"hold-out read, not confirmed: {confirming.Verdict}{(Sound(confirming, "KEEP") is { } flaw ? "; " + flaw : string.Empty)}{MissingText(confirming)}");
            case "INCONCLUSIVE" or "NOT_EVALUABLE":
                return Open(family, development, development.Verdict, holdOut, development.Missing.Count > 0 ? "missing: " + string.Join("; ", development.Missing) : "no missing quantity named");
            default:
                return Open(family, development, "NOT_EVALUABLE", holdOut, "unrecognised verdict word");
        }
    }

    private static OptionsFamilyLine Line(string family, OptionsRecordFact record, string status, bool resolved, string holdOut, string detail) =>
        new(family, status, resolved, false, record.Id, record.Trials, record.Basis, holdOut, record.DerivedExpiryShare, record.FallbackShare, detail);

    private static OptionsFamilyLine Open(string family, OptionsRecordFact record, string status, string holdOut, string detail) => Line(family, record, status, false, holdOut, detail);

    // Null when the record is sound evidence for its word; else why it is not: named missing quantities, no rows behind it, or a charge basis the protocol does not accept.
    private static string? Sound(OptionsRecordFact record, string word)
    {
        if (record.Missing.Count > 0 || record.Rows <= 0)
        {
            return $"thin evidence: {word} with {(record.Missing.Count > 0 ? "named missing quantities" : "no rows behind it")}{MissingText(record)}";
        }

        // The pairs study (protocol S) may DROP gross (gross returns overstate net ones, so a gross DROP stands; ADR 0196); the comparison (protocol X) states no basis, so its DROP stands and its KEEP needs a stored net; O and R need net for both words.
        var basisOk = record.Basis == "net" || (word == "DROP" && record.Protocol is "S" or "X" && record.Basis != "mixed");
        return basisOk ? null : $"costs are {record.Basis}, net is required for a {word}";
    }

    private static string MissingText(OptionsRecordFact record) => record.Missing.Count > 0 ? "; missing: " + string.Join("; ", record.Missing) : string.Empty;

    // Null when the record is for the current versions and the archive has not moved on from its watermark by more than the bound (measured against the newest finished fetch, not the clock, so a stopped archive does not age a verdict); else the first difference in words.
    private static string? Stale(OptionsRecordFact record, DateTimeOffset? latestFetch, int staleAfterDays)
    {
        foreach (var (key, current) in record.Current.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!record.Recorded.TryGetValue(key, out var recorded))
            {
                return $"{key} not recorded, current {current}";
            }

            if (!string.Equals(recorded, current, StringComparison.Ordinal))
            {
                return $"{key} {recorded}, current {current}";
            }
        }

        if (record.Watermark is { Id: > 0 } mark)
        {
            if (mark.EndedAt is not { } at)
            {
                return "archive watermark not found";
            }

            if (latestFetch is not { } latest)
            {
                return "no archive fetch is stored";
            }

            if ((latest - at).TotalDays > staleAfterDays)
            {
                return $"archive watermark of {at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is more than {staleAfterDays.ToString(CultureInfo.InvariantCulture)} days behind the newest fetch";
            }
        }

        return null;
    }
}

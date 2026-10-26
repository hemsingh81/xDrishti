using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The P8 phase check (spec P8-17, on the pattern of ADR 0097): the exit of P8-00 judged from facts that were read. A pure function with hand tables. MET is earned by evidence; a criterion that could not be read is NOT EVALUABLE, never MET; a MET that
/// rests on a convention carries its caveat. The owner's stopwatch is the owner's act: it enters as a recorded note and is never invented here.
/// </summary>
public static class ReportsPhaseCheck
{
    /// <summary>Seconds the owner's stopwatch may show per trial.</summary>
    public const double MaxSeconds = 120;

    /// <summary>Interactions the answer may take.</summary>
    public const int MaxInteractions = 4;

    /// <summary>Milliseconds a request of the answer path may take.</summary>
    public const long MaxRequestMs = 1000;

    /// <summary>Trials the owner's note needs.</summary>
    public const int RequiredTrials = 3;

    /// <summary>The route prefixes that must stay read-only.</summary>
    public static IReadOnlyList<string> ReadOnlyPrefixes { get; } =
        ["/api/reports", "/api/learning", "/api/cells", "/api/plans/notices", "/api/changelog", "/api/lessons", "/api/hypotheses", "/api/experiments", "/api/exports"];

    /// <summary>The reports the check calls.</summary>
    public static IReadOnlyList<string> BuiltReports { get; } =
        ["summary", "groups", "pivot", "equity", "distribution", "mae-mfe", "insights", "what-works", "trades"];

    /// <summary>The limits printed with every report.</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "the owner's stopwatch measures one person on one machine; a faster second visit is not a cold start, and the note is recorded, never verified",
        "on about 2.1 years of data the honest answer is likely \"nothing has enough evidence yet\"; that is an answer, and the exit proves the machinery and its honesty, not that a winning cell exists",
        "timings on a fixture say little about the owner's database; the real-data times are reported here, not asserted",
        "the check reads the data a screen is given; that a screen renders every state correctly is proved by the per-screen tests, not here",
        "the interaction test, the stopwatch note, the bundle size and the browser pass are files on the owner's machine: the server cannot read them, scripts/p8-phase-check.sh does",
    ];

    private const string Spec = "scripts/p8-phase-check.sh";

    /// <summary>Judges the facts.</summary>
    /// <param name="facts">What was read.</param>
    public static ReportsPhaseReport Evaluate(ReportsPhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        PhaseCriterion[] criteria =
        [
            ExitQuestion(facts),
            Evidence(facts),
            States(facts),
            NoDoubleCounting(facts),
            ReadOnly(facts),
            new(6, "Layers and contract", PhaseVerdict.NotEvaluable, "proved by XDrishti.Architecture.Tests and scripts/gen-api.sh, not by this check"),
            new(7, "Bundle", PhaseVerdict.NotEvaluable, $"read from data/p8-evidence/bundle.json by {Spec}; the server cannot see the frontend build"),
            new(8, "Responsive and accessible", PhaseVerdict.NotEvaluable, $"read from data/p8-evidence/browser-checklist.jsonl by {Spec}; the server cannot see the browser pass"),
            DeterminismAndSecurity(facts),
            new(10, "Gates", PhaseVerdict.NotEvaluable, "scripts/check.sh and the reviewer pass are run by the builder, not by this check"),
        ];
        return new ReportsPhaseReport(criteria, facts.Answer, facts.Timings, facts.Runs, facts.Trades, facts.BasketsWithTrades, BuiltReports, Limits);
    }

    /// <summary>The time rule on the answer-path timings: NotMet when any call is over a second, Met when all are read and within, null when any is missing.</summary>
    /// <param name="timings">The timed reads.</param>
    public static PhaseVerdict? ServerTiming(IReadOnlyList<ReportsPhaseTiming> timings)
    {
        ArgumentNullException.ThrowIfNull(timings);
        var path = timings.Where(t => t.AnswerPath).ToList();
        var values = path.SelectMany(t => new[] { t.ColdMs, t.WarmMs }).ToList();
        if (values.Any(v => v > MaxRequestMs))
        {
            return PhaseVerdict.NotMet;
        }

        return path.Count == 0 || values.Any(v => v is null) ? null : PhaseVerdict.Met;
    }

    private static PhaseCriterion ExitQuestion(ReportsPhaseFacts f)
    {
        const string name = "The exit question in under two minutes";
        var c = CultureInfo.InvariantCulture;
        if (f.Runs == 0)
        {
            return new(1, name, PhaseVerdict.NotEvaluable, NoRun(f, "the answer path has nothing to time"));
        }

        var server = ServerTiming(f.Timings);
        var trials = f.Stopwatch?.Trials ?? [];
        var over = trials.Count(t => t.Seconds > MaxSeconds);
        var tooMany = trials.Count(t => t.Interactions > MaxInteractions);
        var fails = new List<string>();
        if (server == PhaseVerdict.NotMet)
        {
            fails.Add("a request of the answer path took over 1 s");
        }

        if (f.InteractionTestPassed == false)
        {
            fails.Add("the interaction test failed");
        }

        if (over > 0)
        {
            fails.Add(string.Create(c, $"{over} stopwatch trials over {MaxSeconds:0} s"));
        }

        if (tooMany > 0)
        {
            fails.Add(string.Create(c, $"{tooMany} stopwatch trials over {MaxInteractions} interactions"));
        }

        if (fails.Count > 0)
        {
            return new(1, name, PhaseVerdict.NotMet, string.Join("; ", fails));
        }

        var missing = new List<string>();
        if (server is null)
        {
            missing.Add("the server timing of the answer path");
        }

        if (f.InteractionTestPassed is null)
        {
            missing.Add("a current interaction test result");
        }

        if (f.Stopwatch is null)
        {
            missing.Add("the owner's stopwatch note for this commit");
        }
        else if (trials.Count < RequiredTrials || f.Stopwatch.Date is null)
        {
            missing.Add($"a complete stopwatch note ({RequiredTrials} trials and a date)");
        }

        if (missing.Count > 0)
        {
            return new(1, name, PhaseVerdict.NotEvaluable, "missing: " + string.Join(", ", missing));
        }

        return new(1, name, PhaseVerdict.Met, string.Create(c, $"answer path within 1 s; interaction test passed; {trials.Count} stopwatch trials each within {MaxSeconds:0} s and {MaxInteractions} interactions, dated {f.Stopwatch!.Date:yyyy-MM-dd}"),
            "the stopwatch note is the owner's own, recorded and not verified; the interaction test runs on a fixture");
    }

    private static PhaseCriterion Evidence(ReportsPhaseFacts f)
    {
        const string name = "No number without its evidence";
        if (f.Runs == 0)
        {
            return new(2, name, PhaseVerdict.NotEvaluable, NoRun(f, "nothing to read"));
        }

        var lacking = f.Reports.Where(r => r.Readable && !r.EvidenceComplete).Select(r => r.Name).ToList();
        if (lacking.Count > 0)
        {
            return new(2, name, PhaseVerdict.NotMet, "evidence block incomplete in: " + string.Join(", ", lacking));
        }

        var unread = f.Reports.Where(r => !r.Readable).Select(r => $"{r.Name} ({r.Problem})").ToList();
        if (unread.Count > 0 || f.Reports.Count == 0)
        {
            return new(2, name, PhaseVerdict.NotEvaluable, f.Reports.Count == 0 ? "no report was read" : "could not be read: " + string.Join(", ", unread));
        }

        return new(2, name, PhaseVerdict.Met, string.Create(CultureInfo.InvariantCulture, $"{f.Reports.Count} reports each carry basis, n, run keys and limits"));
    }

    private static PhaseCriterion States(ReportsPhaseFacts f)
    {
        const string name = "No green-washing";
        if (f.Runs == 0)
        {
            return new(3, name, PhaseVerdict.NotEvaluable, NoRun(f, "nothing to read"));
        }

        if (f.MetricBreaks.Count > 0)
        {
            return new(3, name, PhaseVerdict.NotMet, string.Create(CultureInfo.InvariantCulture, $"{f.MetricBreaks.Count} figures break the state rule, first: {string.Join("; ", f.MetricBreaks.Take(3))}"));
        }

        if (f.MetricsScanned == 0 || f.Reports.Any(r => !r.Readable))
        {
            return new(3, name, PhaseVerdict.NotEvaluable, f.MetricsScanned == 0 ? "no figure was found to scan" : "a report could not be read, so its figures were not scanned");
        }

        return new(3, name, PhaseVerdict.Met, string.Create(CultureInfo.InvariantCulture, $"{f.MetricsScanned} figures: a number only in value or provisional and finite; every other state has none and a reason"),
            "screen rendering is proved by the per-screen tests, not here");
    }

    private static PhaseCriterion NoDoubleCounting(ReportsPhaseFacts f)
    {
        const string name = "No double counting";
        var failed = new List<string>();
        if (!f.OverlapRefused)
        {
            failed.Add("two overlapping runs were not refused (reports.run_overlap)");
        }

        if (!f.MixedBasisRefused)
        {
            failed.Add("a gross and a costed run were not refused (reports.mixed_basis)");
        }

        if (f.DefaultSetValid == false)
        {
            failed.Add("the default set built from the stored runs is not valid");
        }

        if (failed.Count > 0)
        {
            return new(4, name, PhaseVerdict.NotMet, string.Join("; ", failed));
        }

        if (!f.RunsRead)
        {
            return new(4, name, PhaseVerdict.NotEvaluable, "the probes passed but the list of runs could not be read");
        }

        if (f.Runs > 0 && f.DefaultSetValid is null)
        {
            return new(4, name, PhaseVerdict.NotEvaluable, "the probes passed but the default set could not be read");
        }

        return f.Runs == 0
            ? new(4, name, PhaseVerdict.Met, "both probe sets are refused", "the default set was not checked: no stored run")
            : new(4, name, PhaseVerdict.Met, "both probe sets are refused and the default set is valid");
    }

    private static PhaseCriterion ReadOnly(ReportsPhaseFacts f)
    {
        const string name = "Read-only P8";
        if (f.Routes is null)
        {
            return new(5, name, PhaseVerdict.NotEvaluable, "this host cannot show its route table (run the verb against the API, or call the endpoint)");
        }

        var scanned = f.Routes.Where(r => ReadOnlyPrefixes.Any(p => Under(r.Pattern, p))).ToList();
        if (scanned.Count == 0)
        {
            return new(5, name, PhaseVerdict.NotEvaluable, "no route under the guarded prefixes was visible");
        }

        var writes = scanned.Where(r => r.Method != "GET" && !ApprovedReportWrites.All.Contains($"{r.Method} {r.Pattern}", StringComparer.OrdinalIgnoreCase)).Select(r => $"{r.Method} {r.Pattern}").ToList();
        return writes.Count > 0
            ? new(5, name, PhaseVerdict.NotMet, "not read-only: " + string.Join(", ", writes))
            : new(5, name, PhaseVerdict.Met, string.Create(CultureInfo.InvariantCulture, $"{scanned.Count} routes under the guarded prefixes, GET only ({ApprovedReportWrites.All.Count} approved writes)"), "frontend folders are checked by the no-write test, not here");
    }

    private static PhaseCriterion DeterminismAndSecurity(ReportsPhaseFacts f)
    {
        const string name = "Determinism and security";
        if (f.Runs == 0 || f.Deterministic is null)
        {
            return new(9, name, PhaseVerdict.NotEvaluable, f.Runs == 0 ? NoRun(f, "identical empty bodies prove nothing") : "the repeat read was not made for every built report: " + f.DeterminismNote);
        }

        var failed = new List<string>();
        if (f.Deterministic == false)
        {
            failed.Add("the same request twice gave different bodies" + (f.DeterminismNote is null ? string.Empty : " (" + f.DeterminismNote + ")"));
        }

        if (f.SensitiveFieldNames.Count > 0)
        {
            failed.Add("field names with an account, client, token or password word: " + string.Join(", ", f.SensitiveFieldNames.Take(5)));
        }

        return failed.Count > 0
            ? new(9, name, PhaseVerdict.NotMet, string.Join("; ", failed))
            : new(9, name, PhaseVerdict.Met, "the same request twice gave identical bodies for every built report", "names of fields, not content, were scanned for account, client, token and password words");
    }

    private static string NoRun(ReportsPhaseFacts f, string consequence) =>
        f.RunsRead ? "no stored run: " + consequence : "the list of runs could not be read: " + consequence;

    private static bool Under(string pattern, string prefix) =>
        pattern.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (pattern.Length == prefix.Length || pattern[prefix.Length] is '/' or '{');
}

using System.Globalization;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The phase check of P7 (spec P7-18, ADR 0097): the acceptance criteria of P7-00 judged from stored facts, and the roadmap exit "plans published 10 consecutive sessions unattended" read as P7-00 question 10 defines it. A pure function with hand tables.
/// MET on the exit needs ten consecutive trading sessions, each with a published plan from a scheduled (not manual) run and no failed-closed attempt; anything less is NOT MET (a fact breaks the run) or NOT EVALUABLE (history too short). The criteria that tests prove
/// (no look-ahead, determinism, the active set) are NOT EVALUABLE here: this check reads records and runs no test.
/// </summary>
public static class PlanPhaseCheck
{
    /// <summary>The number of consecutive sessions the roadmap exit asks for.</summary>
    public const int RequiredSessions = 10;

    /// <summary>The newest run rows read for the history and the fail-closed counts.</summary>
    public const int RunRowsRead = 200;

    /// <summary>The newest plans whose tickets, approvals, grades and journal are counted.</summary>
    public const int PlansRead = 60;

    /// <summary>The autonomy settings rows read (the store's own ceiling).</summary>
    public const int AutonomyRowsRead = 200;

    public const string StateCounted = "counted";

    public const string StateManual = "manual_run";

    public const string StateFailed = "failed_closed";

    public const string StateNoRun = "no_run";

    public const string StatePlanMissing = "plan_missing";

    public const string StateBeforeHistory = "before_history";

    /// <summary>The gates in the order the pipeline reads them.</summary>
    public static readonly IReadOnlyList<string> GateOrder =
    [
        NightlyGateIds.Calendar, NightlyGateIds.Eod, NightlyGateIds.PortfolioSync, NightlyGateIds.FreshData, NightlyGateIds.Quality, NightlyGateIds.SelectionInputs,
    ];

    /// <summary>The broker allow-list as pinned when the slice was built (ADR 0097): read-only data endpoints (P11-01 added the two reads of the day's trade book and order book, ADR 0149) and the consent flow, no order write, position or fund endpoint. A change is an owner-approved item and must change this list in the same commit.</summary>
    public static readonly IReadOnlyList<string> PinnedAllowList =
    [
        "GET api.dhan.co/v2/holdings",
        "GET api.dhan.co/v2/ledger?from-date&to-date",
        "GET api.dhan.co/v2/orders",
        "GET api.dhan.co/v2/trades",
        "GET api.dhan.co/v2/trades/{from}/{to}/{page}",
        "POST api.dhan.co/v2/charts/intraday",
        "POST api.dhan.co/v2/marketfeed/quote",
        "POST auth.dhan.co/app/consumeApp-consent?tokenId",
        "POST auth.dhan.co/app/generate-consent?client_id",
        "WSS api-feed.dhan.co?authType&clientId&token&version",
        "WSS depth-api-feed.dhan.co/twentydepth?authType&clientId&token",
    ];

    /// <summary>Requests that name an order, position or fund endpoint; the allow-list must refuse every one.</summary>
    public static readonly IReadOnlyList<(string Method, string Url)> RefusedProbes =
    [
        ("POST", "https://api.dhan.co/v2/orders"),
        ("GET", "https://api.dhan.co/v2/orders/1"),
        ("GET", "https://api.dhan.co/v2/orders?x=1"),
        ("POST", "https://api.dhan.co/v2/trades"),
        ("PUT", "https://api.dhan.co/v2/orders/1"),
        ("DELETE", "https://api.dhan.co/v2/orders/1"),
        ("POST", "https://api.dhan.co/v2/super/orders"),
        ("POST", "https://api.dhan.co/v2/forever/orders"),
        ("GET", "https://api.dhan.co/v2/positions"),
        ("GET", "https://api.dhan.co/v2/fundlimit"),
        ("POST", "https://api.dhan.co/v2/killswitch"),
        ("POST", "https://api.dhan.co/v2/marketfeed/ltp"),
    ];

    /// <summary>Judges the criteria from the stored facts.</summary>
    public static PlanPhaseReport Evaluate(PlanPhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var sessions = Classify(facts);
        var streak = Streak(sessions);
        var criteria = new List<PlanPhaseCriterion>
        {
            TenSessions(facts, sessions, streak),
            FailClosed(facts.FailClosed),
            ByTests(3, "No look-ahead: tickets for D+1 are identical when data after the close of D is removed; evidence has as_of not after D", "proved by the truncation and as-of tests (ReturnSeriesTests, EligibilityFiltersTests, SelectionPlanBuilderTests), not by a stored record; this check runs no test"),
            NoOrder(facts.AllowList),
            ByTests(5, "Selection is explainable and deterministic: the same inputs give the same plan, every non-primary candidate has a reason code", "proved by the hand tables and determinism tests (EligibilityFiltersTests, MergeAndRankTests, PortfolioFillerTests, WiredPlanEndToEndTests); this check runs no test"),
            Evidence(facts.Evidence),
            Review(facts.Review),
            Accounts(facts.Allocation),
            Grading(facts.Grades),
            ByTests(10, "The active set: an armed ticket's instrument is in the feed's set and leaves it when the ticket ends", "a live-feed property proved by the pre-open and active-set tests (PreOpenServiceTests, LiveFeedArchitectureTests), not by a stored record; this check runs no test"),
            Journal(facts.Journal),
            ByTests(12, "Every slice ends with scripts/check.sh green, a gen-api run, a reviewer pass, an ADR and build-docs", "a process step of the build, not a stored record"),
        };

        return new PlanPhaseReport(facts.Through, streak, criteria[0].Verdict, PlanPhaseReport.Label(criteria[0]), criteria, sessions, facts.Setup.Services, facts.Setup.Switches, Blockers(facts.Setup), facts.Setup.Counts, Limits());
    }

    /// <summary>One line per planned session of the window, oldest first.</summary>
    public static IReadOnlyList<PlanPhaseSession> Classify(PlanPhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return [.. facts.Window.Select(s => Classify(s, facts.FirstRecorded))];
    }

    private static PlanPhaseSession Classify(PhaseSessionInput s, DateOnly? firstRecorded)
    {
        var runs = s.Runs.OrderBy(r => r.Id).ToList();
        var scheduled = runs.Count(r => !r.Manual);
        var manual = runs.Count(r => r.Manual);
        var failed = runs.Count(r => r.Outcome == "failed_closed");
        var published = runs.FirstOrDefault(r => r.Outcome == "published");
        var deciding = published ?? runs.LastOrDefault();
        var plan = published is not null ? s.Plan : null;
        var zero = plan is not null && plan.Primary == 0;
        var skeleton = plan is not null && plan.Basis.StartsWith(PlanWording.SkeletonBasisPrefix, StringComparison.Ordinal);

        var (state, note) = (runs.Count, manual, failed, published, plan) switch
        {
            (0, _, _, _, _) when firstRecorded is null || s.Date < firstRecorded => (StateBeforeHistory, "no run recorded yet: the pipeline has no history this far back"),
            (0, _, _, _, _) => (StateNoRun, "no run recorded: the nightly did not run for this session (a gap breaks the run)"),
            (_, > 0, _, _, _) => (StateManual, $"{manual} manual run(s): a person started the pipeline, so the session is not unattended"),
            (_, _, > 0, _, _) => (StateFailed, "failed closed: " + FailureText(runs.Last(r => r.Outcome == "failed_closed"))),
            (_, _, _, null, _) => (StateNoRun, "runs are recorded but none published a plan"),
            (_, _, _, _, null) => (StatePlanMissing, "the published row names a plan that is not stored"),
            _ => (StateCounted, "scheduled run published a plan"),
        };

        return new PlanPhaseSession(
            s.Date,
            state,
            scheduled,
            manual,
            failed,
            published is not null,
            plan?.Id ?? published?.PlanId,
            plan?.Primary,
            plan?.Reserve,
            plan?.Rejected,
            zero,
            skeleton,
            GatesText(deciding, published is not null),
            deciding?.FinishedAt,
            note);
    }

    private static string GatesText(PhaseRun? run, bool published)
    {
        if (run is null)
        {
            return "-";
        }

        if (published)
        {
            return $"{GateOrder.Count}/{GateOrder.Count} pass";
        }

        var index = run.FailedGate is null ? -1 : GateOrder.ToList().IndexOf(run.FailedGate);
        return index >= 0
            ? $"{index} pass, {run.FailedGate} did not pass"
            : $"{GateOrder.Count} pass, stage {(run.FailedGate is null ? "unknown" : Clean(run.FailedGate, GateResult.MaxGateLength))} failed";
    }

    private static string FailureText(PhaseRun run) => $"{(run.FailedGate is null ? "unknown" : Clean(run.FailedGate, GateResult.MaxGateLength))}: {Clean(run.Reason, 160)}";

    private static string Clean(string text, int max)
    {
        var flat = new string([.. text.Select(ch => char.IsControl(ch) ? ' ' : ch)]);
        if (flat.Length <= max)
        {
            return flat;
        }

        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(flat[cut - 1]))
        {
            cut--; // never leave half of a surrogate pair
        }

        return flat[..cut] + "~";
    }

    private static int Streak(IReadOnlyList<PlanPhaseSession> sessions)
    {
        var streak = 0;
        for (var i = sessions.Count - 1; i >= 0 && sessions[i].State == StateCounted; i--)
        {
            streak++;
        }

        return streak;
    }

    private static PlanPhaseCriterion TenSessions(PlanPhaseFacts facts, IReadOnlyList<PlanPhaseSession> sessions, int streak)
    {
        const string name = "Plans published 10 consecutive trading sessions unattended (roadmap exit)";
        var c = CultureInfo.InvariantCulture;
        var breaks = sessions.Where(s => s.State is not (StateCounted or StateBeforeHistory)).ToList();
        var before = sessions.Count(s => s.State == StateBeforeHistory);
        var manualRuns = sessions.Sum(s => s.ManualRuns);
        var zero = sessions.Count(s => s.ZeroPrimary);
        var facts1 = string.Create(c, $"{sessions.Count(s => s.State == StateCounted)} of {sessions.Count} sessions up to {facts.Through:yyyy-MM-dd} counted, {streak} in a row at the end, manual runs in the window {manualRuns}, failed-closed attempts {sessions.Sum(s => s.FailedRuns)}, plans with zero primary tickets {zero}, skeleton plans {sessions.Count(s => s.Skeleton)}");

        if (sessions.Count != RequiredSessions)
        {
            return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.NotEvaluable, string.Create(c, $"{facts1}; the window has {sessions.Count} trading sessions, exactly {RequiredSessions} are needed."));
        }

        var problem = WindowProblem(facts, sessions);
        if (problem is not null)
        {
            return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.NotEvaluable, facts1 + "; the window is malformed (" + problem + "), so no call is made.");
        }

        if (breaks.Count > 0)
        {
            var list = string.Join("; ", breaks.Select(b => string.Create(c, $"{b.Date:yyyy-MM-dd} {b.State}")));
            return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.NotMet, string.Create(c, $"{facts1}; {breaks.Count} session(s) break the run: {list}. The run starts again after the newest break."));
        }

        if (before > 0)
        {
            return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.NotEvaluable, string.Create(c, $"{facts1}; only {sessions.Count - before} of {RequiredSessions} sessions have any history and all of them are good so far: {before} more trading session(s) of scheduled runs are needed (wall-clock time)."));
        }

        // Defensive: MET only when exactly ten sessions are counted (checked above) - spelled out so no edit can print MET on less.
        if (sessions.Count != RequiredSessions || sessions.Any(s => s.State != StateCounted || s.ManualRuns > 0 || s.FailedRuns > 0 || !s.Published))
        {
            return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.NotEvaluable, facts1 + "; internal inconsistency.");
        }

        var empty = sessions.Count(s => s is { Primary: 0, Reserve: 0 });
        var caveat = empty == sessions.Count
            ? string.Create(c, $"COUNT ONLY: all {sessions.Count} plans are empty, no ticket was ever proposed")
            : zero > 0 ? string.Create(c, $"{zero} of {sessions.Count} plans have zero primary tickets, {sessions.Count(s => s.Skeleton)} of them skeleton") : null;
        return new PlanPhaseCriterion(1, name, PlanPhaseVerdicts.Met, facts1 + "; unattended means: scheduled or catch-up runs only (the stored manual flag), no failed-closed attempt, in the trading calendar's order (a holiday between two sessions does not break the run).", caveat);
    }

    /// <summary>Why the window cannot be trusted as ten consecutive trading sessions ending at the requested date; null when it can.</summary>
    private static string? WindowProblem(PlanPhaseFacts facts, IReadOnlyList<PlanPhaseSession> sessions)
    {
        for (var i = 1; i < sessions.Count; i++)
        {
            if (sessions[i].Date <= sessions[i - 1].Date)
            {
                return "dates are not strictly ascending";
            }
        }

        return sessions[^1].Date == facts.Through ? null : "the last date is not the requested one";
    }

    private static PlanPhaseCriterion FailClosed(PhaseFailClosedFacts f)
    {
        const string name = "Fail closed: a gate that does not pass publishes no plan, and one run row carries the gate and the reason";
        var c = CultureInfo.InvariantCulture;
        var failed = f.ByGate.Sum(g => g.Count);
        var gates = f.ByGate.Count == 0 ? "none" : string.Join(", ", f.ByGate.Select(g => $"{Clean(g.Gate, GateResult.MaxGateLength)} {g.Count}"));
        var facts = string.Create(c, $"{failed} failed-closed row(s) in the newest {f.RowsRead} run rows{(f.Truncated ? " (older rows exist)" : string.Empty)}; per gate: {gates}");
        if (f.Inconsistent > 0)
        {
            return new PlanPhaseCriterion(2, name, PlanPhaseVerdicts.NotMet, string.Create(c, $"{facts}; {f.Inconsistent} of them lack a gate or a reason or name a plan."));
        }

        if (failed == 0)
        {
            return new PlanPhaseCriterion(2, name, PlanPhaseVerdicts.NotEvaluable, facts + "; no fail-closed day has happened yet, so no stored row shows the behaviour. Each gate's test (NightlyPipelineTests) is the proof; this check runs no test.");
        }

        var unseen = GateOrder.Where(g => f.ByGate.All(x => x.Gate != g)).ToList();
        return unseen.Count > 0
            ? new PlanPhaseCriterion(2, name, PlanPhaseVerdicts.NotEvaluable, facts + "; every such row is consistent, but " + string.Join(", ", unseen) + " has not been seen failing in a stored row: the per-gate proof is the tests (NightlyPipelineTests), which this check does not run.")
            : new PlanPhaseCriterion(2, name, PlanPhaseVerdicts.Met, facts + "; every such row carries its gate and reason and names no plan, and all " + GateOrder.Count + " gates have been seen failing.", "the per-gate proof is NightlyPipelineTests, not run here");
    }

    private static PlanPhaseCriterion NoOrder(PhaseAllowListFacts f)
    {
        const string name = "No order, ever: the broker allow-list is unchanged and has no order endpoint; the architecture rule passes";
        var c = CultureInfo.InvariantCulture;
        if (f.Current.Count == 0)
        {
            return new PlanPhaseCriterion(4, name, PlanPhaseVerdicts.NotEvaluable, "the allow-list could not be read.");
        }

        var added = f.Current.Except(f.Pinned, StringComparer.Ordinal).ToList();
        var removed = f.Pinned.Except(f.Current, StringComparer.Ordinal).ToList();
        if (f.ProbesAllowed.Count > 0 || added.Count > 0 || removed.Count > 0)
        {
            var parts = new List<string>();
            if (f.ProbesAllowed.Count > 0)
            {
                parts.Add("allowed although they name an order, position or fund endpoint: " + string.Join(", ", f.ProbesAllowed));
            }

            if (added.Count > 0)
            {
                parts.Add("added to the pinned list: " + string.Join(", ", added));
            }

            if (removed.Count > 0)
            {
                parts.Add("missing from the code: " + string.Join(", ", removed));
            }

            return new PlanPhaseCriterion(4, name, PlanPhaseVerdicts.NotMet, "the allow-list differs from the pinned one (a change needs the owner's approval and a new pin): " + string.Join("; ", parts) + ".");
        }

        return new PlanPhaseCriterion(
            4,
            name,
            PlanPhaseVerdicts.Met,
            string.Create(c, $"the allow-list has the {f.Current.Count} pinned entries and no entry beyond those the guard's rule table declares; all {RefusedProbes.Count} order, position and fund probes are refused."),
            "the architecture tripwire (NoOrderPlacementArchitectureTests) is a test, not run here");
    }

    private static PlanPhaseCriterion Evidence(PhaseEvidenceFacts f)
    {
        const string name = "Honest evidence on the plan: every plan says no order exists and what it rests on; every ticket carries its cell, data range and probability basis";
        var c = CultureInfo.InvariantCulture;
        var facts = string.Create(c, $"{f.Plans} plan(s) and {f.Tickets} ticket(s) read; plans without the no-order sentence {f.PlansWithoutNotice}, without the limits {f.PlansWithoutLimits}, tickets without a data range or probability basis {f.TicketsWithoutEvidence}");
        if (f.Plans == 0)
        {
            return new PlanPhaseCriterion(6, name, PlanPhaseVerdicts.NotEvaluable, "no plan is stored.");
        }

        if (f.PlansWithoutNotice + f.PlansWithoutLimits + f.TicketsWithoutEvidence > 0)
        {
            return new PlanPhaseCriterion(6, name, PlanPhaseVerdicts.NotMet, facts + ".");
        }

        return f.Tickets == 0
            ? new PlanPhaseCriterion(6, name, PlanPhaseVerdicts.NotEvaluable, facts + "; the plans are empty, so there is no ticket evidence to check.")
            : new PlanPhaseCriterion(6, name, PlanPhaseVerdicts.Met, facts + ".");
    }

    private static PlanPhaseCriterion Review(PhaseReviewFacts f)
    {
        const string name = "Human in the loop: unreviewed tickets lapse at the cutoff, only an approved ticket is armed, every decision is a stored row";
        var c = CultureInfo.InvariantCulture;
        var cap = f.PlansCap > 0 ? string.Create(c, $" (the newest {f.PlansCap} at most{(f.PlansTruncated ? "; older plans are not read" : string.Empty)})") : string.Empty;
        var facts = string.Create(c, $"{f.Plans} plan(s) read{cap}: lapsed {f.PlansLapsed}, with a decision {f.PlansWithDecision}; tickets: owner approved {f.OwnerApproved}, modified {f.OwnerModified}, rejected {f.OwnerRejected}, auto approved {f.AutoApproved}, lapsed {f.TicketsLapsed}");
        var problems = new List<string>();
        if (f.AutoBeforeFirstOn > 0)
        {
            problems.Add(string.Create(c, $"{f.AutoBeforeFirstOn} auto approval(s) dated before the switch was first stored ON (or the switch was never ON)"));
        }

        if (f.DecisionsAfterCutoff > 0)
        {
            problems.Add(string.Create(c, $"{f.DecisionsAfterCutoff} decision(s) dated at or after the cutoff"));
        }

        if (f.ApprovedWithoutRow > 0)
        {
            problems.Add(string.Create(c, $"{f.ApprovedWithoutRow} ticket(s) that were approved, armed or triggered (now or earlier in their trail) without an approve row"));
        }

        if (f.UndecidedPastCutoff > 0)
        {
            problems.Add(string.Create(c, $"{f.UndecidedPastCutoff} ticket(s) still undecided well after the cutoff (the review-cutoff job did not lapse them)"));
        }

        var history = f.AutoHistoryTruncated ? " The autonomy history is longer than the rows read, so an auto approval before the first ON row cannot be told." : string.Empty;
        if (problems.Count > 0)
        {
            return new PlanPhaseCriterion(7, name, PlanPhaseVerdicts.NotMet, facts + "; " + string.Join("; ", problems) + "." + history);
        }

        var decided = f.OwnerApproved + f.OwnerModified + f.OwnerRejected + f.AutoApproved + f.TicketsLapsed;
        return decided == 0
            ? new PlanPhaseCriterion(7, name, PlanPhaseVerdicts.NotEvaluable, facts + "; no ticket has been reviewed or lapsed yet." + history)
            : new PlanPhaseCriterion(7, name, PlanPhaseVerdicts.Met, facts + "; auto approvals are " + (f.AutoApproved == 0 ? "0" : "recorded as auto, under a switch the owner turned ON") + "." + history, "refusals of bad edits and the owner-only rule are tests (ReviewHandlersTests), not run here");
    }

    private static PlanPhaseCriterion Accounts(PhaseAllocationFacts f)
    {
        const string name = "Accounts: allocation goes only to paper-mode Trading accounts until the owner allows more";
        var c = CultureInfo.InvariantCulture;
        var facts = string.Create(c, $"{f.Allocations} allocation row(s) read, not paper {f.NotPaper}");
        if (f.NotPaper > 0)
        {
            return new PlanPhaseCriterion(8, name, PlanPhaseVerdicts.NotMet, facts + ": a non-paper account received a ticket.");
        }

        return f.Allocations == 0
            ? new PlanPhaseCriterion(8, name, PlanPhaseVerdicts.NotEvaluable, facts + "; no ticket has been sized for an account yet.")
            : new PlanPhaseCriterion(8, name, PlanPhaseVerdicts.Met, facts + ".", "account roles and risk limits are not re-read here; the allocator tests prove them");
    }

    private static PlanPhaseCriterion Grading(PhaseGradeFacts f)
    {
        const string name = "Grading: primary, reserve and rejected tickets of a day are graded once, labelled 'simulated on plan prices'";
        var c = CultureInfo.InvariantCulture;
        var facts = string.Create(c, $"{f.GradedPlans} plan(s) graded, {f.GradedTickets} of {f.TicketsOfGradedPlans} tickets of those plans have a grade ({f.PartlyGradedPlans} plan(s) graded in part: a ticket that cannot be replayed yet stays pending and is not written; the store keeps the first grade of a ticket, so a second cannot be stored)");
        return f.GradedPlans == 0
            ? new PlanPhaseCriterion(9, name, PlanPhaseVerdicts.NotEvaluable, facts + "; nothing has been graded yet.")
            : new PlanPhaseCriterion(9, name, PlanPhaseVerdicts.Met, facts + ".", "grades are re-simulations on plan prices, not execution; the hand reproduction of the curves is a test (GradeReportTests)");
    }

    private static PlanPhaseCriterion Journal(PhaseJournalFacts f)
    {
        const string name = "Auto-journal: fills are matched to tickets by account, instrument, side and day, never double-counted";
        var c = CultureInfo.InvariantCulture;
        var facts = string.Create(c, $"{f.Entries} entr(ies), {f.Fills} fill link(s), {f.FillGrades} fill grade(s), fills linked twice {f.DuplicateFills}");
        if (f.DuplicateFills > 0)
        {
            return new PlanPhaseCriterion(11, name, PlanPhaseVerdicts.NotMet, facts + ": a fill is counted more than once.");
        }

        return f.Entries == 0
            ? new PlanPhaseCriterion(11, name, PlanPhaseVerdicts.NotEvaluable, facts + "; no ticket has been matched to a fill yet (paper tickets have none).")
            : new PlanPhaseCriterion(11, name, PlanPhaseVerdicts.Met, facts + ".", "the matching rules are tests (JournalPlansHandlerTests), not run here");
    }

    private static PlanPhaseCriterion ByTests(int number, string name, string why) =>
        new(number, name, PlanPhaseVerdicts.NotEvaluable, why + ".");

    /// <summary>Which services and switches are still off and what that stops.</summary>
    private static List<string> Blockers(PhaseSetupFacts setup)
    {
        var blockers = new List<string>();
        foreach (var service in setup.Services.Where(s => s.State != "enabled"))
        {
            blockers.Add(service.Required
                ? $"service {service.Id} is {service.State}: it is needed for the exit ({service.Role}), so no plan is published by schedule until the owner enables it"
                : $"service {service.Id} is {service.State}: {service.Role} does not happen until the owner enables it");
        }

        foreach (var sw in setup.Switches.Where(s => s is { On: false, Needed: true }))
        {
            blockers.Add($"switch {sw.Name} is OFF: {sw.Effect}");
        }

        return blockers;
    }

    private static string[] Limits() =>
    [
        "about 2.1 years of data, no hold-out: cell statistics and rules were shaped on the same data, so ten good sessions say the pipeline runs, not that the plans make money (that is the P10 exit)",
        "plans are provisional: no cell is Active before P10, so tickets rest on Candidate cells and every plan says so",
        "the market gate, events, results, F&O ban and surveillance inputs have no data source and read unavailable, never pass",
        "the empty-plan risk: an empty or skeleton plan counts as published (P7-00 question 10) and is reported separately as plans with zero primary tickets; ten empty plans would meet the count and prove only that the pipeline runs",
        "the pre-open quote format is unverified until the first real 09:10 session (ADR 0096); the pre-open check is not part of this exit",
        "unattended is read from the stored manual flag and the absence of failed attempts; a code change or an owner action on the pipeline is not recorded and cannot be seen here",
        "the Dhan Data-account token lapses about daily; a lapse fails the EOD gate and breaks the run, so ten in a row may take more than ten calendar sessions",
        "stored records only: the criteria that tests prove are NOT EVALUABLE here (no test is run), and a MET with a caveat rests on a test or a configuration this check does not run or see",
        "a run the services framework starts as a catch-up after a restart counts as scheduled (only a manual start sets the manual flag); hand-run eod or portfolio-sync runs are not read, only the nightly's own rows",
        "the newest 200 run rows, the newest 60 plans (with their tickets, approvals, grades and journal) and up to 200 autonomy settings rows are read; older records are not",
        "switches marked as read by this process are read from the configuration of the process that ran the check (the host CLI when run with xd); the running containers may be configured differently",
        "nothing is written, trained or changed by this check, and no setting is turned on by it",
    ];
}

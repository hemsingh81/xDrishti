using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The paper phase check (spec P10-06, ADR 0123): the P10 exit "at least 60 graded trades, positive expectancy after costs within the backtest band, drawdown within the 90th percentile, at least 12 weeks" judged from stored facts. A pure function
/// with hand tables. MET needs all four exit criteria and the integrity criterion; every caveat sits in the label; MET is never printed on fewer than 60 counted trades, fewer than 84 days, an unconfirmed charge sheet (E3 and E4 then read not evaluable) or
/// a window that does not hold together. Earlier epochs are listed and never pooled.
/// </summary>
public static class PaperPhaseCheck
{
    /// <summary>Elapsed days of the epoch the exit needs (12 weeks).</summary>
    public const int RequiredDays = PaperReviewBuilder.PhaseDays;

    /// <summary>Counted trades the exit needs.</summary>
    public const int RequiredTrades = PaperReviewBuilder.TargetTrades;

    /// <summary>The share of the epoch's sessions that must have a published plan and a finished capture.</summary>
    public const double RequiredCoverage = 0.80;

    /// <summary>Sessions a pair may stay pending before it counts against integrity.</summary>
    public const int StuckAfterSessions = 3;

    private const string Met = PlanPhaseVerdicts.Met;
    private const string NotMet = PlanPhaseVerdicts.NotMet;
    private const string No = PlanPhaseVerdicts.NotEvaluable;

    /// <summary>What the check never judges, with the tests that prove it (criterion 7).</summary>
    public static IReadOnlyList<PhaseNotJudged> NotJudged { get; } =
    [
        new("no look-ahead", No, "proved by the P3-08 leak properties (XDrishti.Simulation.Tests StateLeakTests, PropertyInventoryTests) and the TicketReplayer tests"),
        new("paper equals grading for the same ticket", No, "proved by CapturePaperFillsHandlerTests.The_paper_entry_equals_the_grade_of_the_same_ticket_because_both_use_the_one_replayer"),
        new("determinism", No, "proved by BacktestBandTests and GetBacktestVsPaperHandlerTests (same inputs, same numbers) and the golden-text tests"),
        new("no order is placed", No, "proved by NoOrderPlacementArchitectureTests, PaperCaptureArchitectureTests and PaperPhaseArchitectureTests"),
        new("real-fill behaviour", No, "not testable on paper: settled by P11"),
    ];

    /// <summary>The honest limits, always printed (criterion 8).</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "Simulated fills on official 1-minute bars with no real execution: a pass says nothing about real fills, slippage, rejections, latency or discipline under real money.",
        "About 2.1 years of history and no hold-out stand behind the cells.",
        "The band and the drawdown reference are a composition-matched bootstrap with the selection and clustering limits of P10-04.",
        "60 trades give an interval of about +-0.3R, so a pass is weak evidence.",
        "The charge sheet confirmation is the owner's act; until it exists the expectancy and drawdown read not evaluable.",
        "Configuration is read from the xd-api process; a change outside the fingerprint, or a change and its reversal between captures, is not seen.",
        "Risk-down changes censor the population: the trades after one are not the trades the cells were chosen on.",
        "Stored records only; nothing is written, no replay is run, no setting is changed. The owner confirms the exit; this check never does.",
    ];

    /// <summary>Judges the facts.</summary>
    /// <param name="facts">The stored facts.</param>
    public static PaperPhaseReport Evaluate(PaperPhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var epochs = facts.History.OrderBy(e => e.Id)
            .Select(e => new PhaseEpochLine(e.Id, e.StartedOn, e.Kind, e.Reason, e.Counted, e.UnexplainedChanges, facts.Epoch is { } c && c.Id == e.Id)).ToList();
        var services = ServiceBlockers(facts.Services);
        if (facts.Epoch is not { } epoch)
        {
            return NoEpoch(facts, epochs, services);
        }

        var elapsed = Math.Max(0, facts.Through.DayNumber - epoch.StartedOn.DayNumber + 1);
        var criteria = new List<PhaseCriterion>
        {
            Duration(facts, elapsed, services),
            Count(facts, elapsed),
            Statistic("C3", "Expectancy within the backtest band, after costs", facts, facts.Exit?.Expectancy),
            Statistic("C4", "Drawdown within the 90th percentile of the backtest", facts, facts.Exit?.Drawdown),
            Integrity(facts),
        };

        var (verdict, label) = Overall(facts, epoch, elapsed, criteria, out var malformed);
        var blockers = criteria.SelectMany(c => c.Blockers.Select(b => c.Id + ": " + b)).Concat(malformed.Select(m => "malformed window: " + m)).Concat(services.Select(s => "services: " + s)).Distinct(StringComparer.Ordinal).ToList();
        return new PaperPhaseReport(
            epochs.Find(e => e.Current), facts.Through, elapsed, criteria, verdict, label, Weeks(facts), epochs, Printed(facts, blockers), Limits);
    }

    private static List<string> ServiceBlockers(IReadOnlyList<PhaseService> services) =>
        [.. services.Where(s => s.Required && s.State != "enabled").Select(s => $"service {s.Id} is {s.State} ({s.Role}): the owner turns it on in the Services screen")];

    private static PaperPhaseReport NoEpoch(PaperPhaseFacts facts, List<PhaseEpochLine> epochs, List<string> services)
    {
        const string why = "no paper epoch exists yet: nothing has been captured";
        PhaseCriterion Gone(string id, string title) => new(id, title, No, No + ": " + why, [why], ["the owner completes the P10-00 pre-flight and the first capture starts the epoch"]);
        var criteria = new List<PhaseCriterion>
        {
            Gone("C1", "Duration and coverage"), Gone("C2", "Counted trades"), Gone("C3", "Expectancy within the backtest band, after costs"), Gone("C4", "Drawdown within the 90th percentile of the backtest"), Gone("I", "Capture integrity"),
        };
        var blockers = criteria.SelectMany(c => c.Blockers.Select(b => c.Id + ": " + b)).Concat(services.Select(s => "services: " + s)).Distinct(StringComparer.Ordinal).ToList();
        return new PaperPhaseReport(null, facts.Through, 0, criteria, No, No, [], epochs, Printed(facts, blockers), Limits);
    }

    private static PhaseCriterion Duration(PaperPhaseFacts facts, int elapsed, List<string> services)
    {
        const string title = "Duration and coverage";
        var c = CultureInfo.InvariantCulture;
        var total = facts.Sessions.Count;
        var covered = facts.Sessions.Count(s => s.PlanPublished && s.CaptureFinished);
        var facing = new List<string> { string.Create(c, $"{elapsed} elapsed days of {RequiredDays}, counted from the epoch start to {facts.Through:yyyy-MM-dd} inclusive"), string.Create(c, $"{covered} of {total} trading sessions have a published plan and a finished capture") };
        if (elapsed < RequiredDays)
        {
            var reason = string.Create(c, $"day {elapsed} of {RequiredDays} (week {Math.Max(1, (elapsed + 6) / 7)} of {RequiredDays / 7})");
            return new PhaseCriterion("C1", title, No, No + ": " + reason, facing, [.. services, "time must pass; nobody can shorten the 12 weeks"]);
        }

        if (total == 0)
        {
            return new PhaseCriterion("C1", title, No, No + ": the window holds no trading session", facing, services);
        }

        var coverage = (double)covered / total;
        var missing = facts.Sessions.Where(s => !(s.PlanPublished && s.CaptureFinished)).Select(s => s.Date.ToString("yyyy-MM-dd", c)).ToList();
        if (coverage < RequiredCoverage)
        {
            facing.Add("missing sessions: " + string.Join(", ", missing));
            return new PhaseCriterion(
                "C1", title, NotMet, string.Create(c, $"{NotMet}: coverage {coverage:0.00} ({covered} of {total} sessions) is below {RequiredCoverage:0.00}"), facing,
                [.. services, "the nightly plan and the paper capture must run each session; keeping them running is the owner's act"]);
        }

        return new PhaseCriterion("C1", title, Met, string.Create(c, $"{Met}: coverage {coverage:0.00} ({covered} of {total} sessions) over {elapsed} days"), facing, services);
    }

    private static PhaseCriterion Count(PaperPhaseFacts facts, int elapsed)
    {
        const string title = "Counted trades";
        var c = CultureInfo.InvariantCulture;
        var n = facts.Counted.Count;
        var facing = new List<string> { string.Create(c, $"{n} of {RequiredTrades} counted trades (closed, primary, measured, once per ticket, this epoch only)") };
        if (n >= RequiredTrades)
        {
            return new PhaseCriterion("C2", title, Met, string.Create(c, $"{Met}: {n} of {RequiredTrades} counted trades"), facing, []);
        }

        if (elapsed >= 7)
        {
            facing.Add(string.Create(c, $"pace {n / (elapsed / 7.0):0.00} counted trades per week"));
        }

        if (n >= PaperReviewBuilder.EtaMinTrades && elapsed >= PaperReviewBuilder.EtaMinDays)
        {
            var perDay = (double)n / elapsed;
            var date = facts.Through.AddDays((int)Math.Ceiling((RequiredTrades - n) / perDay));
            facing.Add(string.Create(c, $"ETA to {RequiredTrades}: around {date:yyyy-MM-dd}, a straight-line guess"));
        }
        else
        {
            facing.Add(string.Create(c, $"ETA to {RequiredTrades}: unavailable (needs at least {PaperReviewBuilder.EtaMinTrades} counted trades and {PaperReviewBuilder.EtaMinDays / 7} weeks elapsed)"));
        }

        return new PhaseCriterion(
            "C2", title, No, string.Create(c, $"{No}: {n} of {RequiredTrades}"), facing,
            ["trades come from approved, armed tickets; if the count lags, widening the universe or rules is a frozen change and a new epoch, the owner's decision"]);
    }

    private static PhaseCriterion Statistic(string id, string title, PaperPhaseFacts facts, CriterionResult? result)
    {
        if (facts.Exit is null || result is null)
        {
            const string why = "the backtest comparison could not be read";
            return new PhaseCriterion(id, title, No, No + ": " + why, [why], ["the comparison (xd paper compare) must be readable"]);
        }

        var verdict = result.Verdict switch
        {
            // A met statistic is only taken at its word when the owner's confirmation of the costs is a fact.
            CriterionResult.Met when !facts.Exit.ChargeSheetConfirmed => No,
            CriterionResult.Met => Met,
            CriterionResult.NotMet => NotMet,
            _ => No,
        };
        const string prefix = "not evaluable: ";
        var reason = result.Label.StartsWith(prefix, StringComparison.Ordinal) ? result.Label[prefix.Length..] : result.Label;
        if (result.Verdict == CriterionResult.Met && verdict == No)
        {
            reason = "costs not confirmed (the comparison's met is not taken at its word)";
        }

        var blockers = new List<string>();
        if (!facts.Exit.ChargeSheetConfirmed)
        {
            blockers.Add("costs not confirmed: the owner's confirmation of the charge sheet is missing or no longer stands (the owner's act: Settings, tab Charge sheet)");
        }

        if (verdict == No && facts.Counted.Count < RequiredTrades)
        {
            blockers.Add("fewer than 60 counted trades");
        }

        var label = verdict == No && !reason.Contains("costs not confirmed", StringComparison.Ordinal) && !facts.Exit.ChargeSheetConfirmed ? reason + "; costs not confirmed" : reason;
        return new PhaseCriterion(id, title, verdict, verdict + ": " + label, [result.Label], blockers);
    }

    private static PhaseCriterion Integrity(PaperPhaseFacts facts)
    {
        const string title = "Capture integrity";
        var c = CultureInfo.InvariantCulture;
        var runs = facts.Sessions.Sum(s => s.CaptureRuns);
        if (runs == 0)
        {
            return new PhaseCriterion("I", title, No, No + ": no capture run of this epoch", ["0 capture runs"], ["the paper-capture service must run"]);
        }

        var facing = new List<string> { string.Create(c, $"{runs} capture rows read") };
        // A mismatch row keeps this NOT MET for the whole epoch even after the pair is resolved: capture rows are insert-only and a later run does not retract one, so only a new epoch clears it (deliberately conservative).
        var mismatched = facts.Sessions.Where(s => s.CaptureIntegrity > 0).ToList();
        var stuck = new List<PhaseSessionFact>();
        for (var i = 0; i < facts.Sessions.Count; i++)
        {
            if (facts.Sessions[i].CapturePending && facts.Sessions.Count - 1 - i > StuckAfterSessions)
            {
                stuck.Add(facts.Sessions[i]);
            }
        }

        if (mismatched.Count == 0 && stuck.Count == 0)
        {
            return new PhaseCriterion("I", title, Met, Met + ": no replay disagreed with a stored entry and nothing stays pending", facing, []);
        }

        var parts = new List<string>();
        if (mismatched.Count > 0)
        {
            var text = string.Create(c, $"{mismatched.Sum(s => s.CaptureIntegrity)} stored entries disagreed with a later replay (sessions {string.Join(", ", mismatched.Select(s => s.Date.ToString("yyyy-MM-dd", c)))})");
            facing.Add(text);
            parts.Add(text);
        }

        if (stuck.Count > 0)
        {
            var text = string.Create(c, $"armed pairs without an outcome for more than {StuckAfterSessions} sessions (sessions {string.Join(", ", stuck.Select(s => s.Date.ToString("yyyy-MM-dd", c)))}); the stored row cannot tell missing bars from a late replay");
            facing.Add(text);
            parts.Add(text);
        }

        return new PhaseCriterion("I", title, NotMet, NotMet + ": " + string.Join("; ", parts), facing, ["the owner investigates the disagreement or the stuck pairs; a stored entry is never overwritten"]);
    }

    private static (string Verdict, string Label) Overall(PaperPhaseFacts facts, PhaseEpochFact epoch, int elapsed, List<PhaseCriterion> criteria, out List<string> malformed)
    {
        malformed = [];
        if (criteria.Any(x => x.Verdict == NotMet))
        {
            return (NotMet, NotMet);
        }

        if (criteria.Any(x => x.Verdict != Met))
        {
            return (No, No);
        }

        var c = CultureInfo.InvariantCulture;

        // These re-checks read facts the criteria above took from other places. Most are unreachable while those criteria hold, but a MET must never depend on them agreeing by luck.
        if (facts.Exit?.ChargeSheetConfirmed != true)
        {
            malformed.Add("the charge sheet is not confirmed by the owner");
        }

        if (facts.Counted.Select(t => t.TicketId).Distinct().Count() != facts.Counted.Count)
        {
            malformed.Add("a ticket is counted twice");
        }

        if (facts.Counted.Count < RequiredTrades)
        {
            malformed.Add(string.Create(c, $"{facts.Counted.Count} counted trades"));
        }

        if (elapsed < RequiredDays)
        {
            malformed.Add(string.Create(c, $"{elapsed} elapsed days"));
        }

        if (facts.Exit?.CompareEpochId != epoch.Id)
        {
            malformed.Add("the comparison was computed for another epoch");
        }

        if (!facts.ThroughIsSession)
        {
            malformed.Add("the end date is not a trading session");
        }

        if (malformed.Count > 0)
        {
            return (No, No + " (malformed window)");
        }

        var caveats = new List<string>();
        if (facts.Exit is not { IntervalLow: not null, IntervalHigh: not null })
        {
            caveats.Add("interval unavailable");
        }

        if (facts.Exit is { IntervalLow: { } lo, IntervalHigh: { } hi } && lo <= 0)
        {
            caveats.Add($"point estimate; 90% interval [{Signed(lo)}, {Signed(hi)}] includes 0");
        }

        if (epoch.RiskDownChanges > 0)
        {
            caveats.Add(string.Create(c, $"{epoch.RiskDownChanges} risk-down {(epoch.RiskDownChanges == 1 ? "change" : "changes")}: the population was censored"));
        }

        var auto = facts.Counted.Count(t => t.ApprovalKind == "auto");
        if (auto > 0)
        {
            caveats.Add(string.Create(c, $"{auto} of {facts.Counted.Count} counted trades were auto-approved"));
        }

        var manual = facts.Sessions.Sum(s => s.ManualPlanRuns);
        if (manual > 0)
        {
            caveats.Add(string.Create(c, $"{manual} manual {(manual == 1 ? "run" : "runs")} of the nightly pipeline in the epoch"));
        }

        if (facts.P7Verdict != Met)
        {
            caveats.Add(facts.P7Verdict is null ? "P7 phase check unavailable" : "P7 phase check not met");
        }

        return (Met, caveats.Count == 0 ? Met : $"{Met} ({string.Join("; ", caveats)})");
    }

    private static List<PhaseWeekLine> Weeks(PaperPhaseFacts facts)
    {
        var tradesByWeek = facts.Counted.GroupBy(t => Monday(IndiaTime.Today(t.ExitAt))).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<PhaseWeekLine>();
        var count = 0;
        var sum = 0.0;
        foreach (var week in facts.Sessions.GroupBy(s => Monday(s.Date)).OrderBy(g => g.Key))
        {
            var trades = tradesByWeek.GetValueOrDefault(week.Key) ?? [];
            count += trades.Count;
            sum += trades.Sum(t => t.R);
            var changes = facts.Changes.Where(x => x.Session is { } d && Monday(d) == week.Key).ToList();
            var year = ISOWeek.GetYear(week.Key.ToDateTime(TimeOnly.MinValue));
            var number = ISOWeek.GetWeekOfYear(week.Key.ToDateTime(TimeOnly.MinValue));
            lines.Add(new PhaseWeekLine(
                string.Create(CultureInfo.InvariantCulture, $"{year}-W{number:00}"), week.Count(), week.Count(s => s.PlanPublished), week.Sum(s => s.FailedClosedRuns), week.Sum(s => s.ManualPlanRuns),
                week.Count(s => s.CaptureFinished), trades.Count, count, sum, changes.Count(x => x.Class == "frozen"), changes.Count(x => x.Class == "risk_down")));
        }

        return lines;
    }

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static PhasePrinted Printed(PaperPhaseFacts facts, List<string> blockers) =>
        new(
            facts.DisciplineLines,
            facts.Exit?.SensitivityMean ?? "unavailable: the backtest comparison could not be read",
            facts.Counted.Count(t => t.ApprovalKind == "auto"),
            facts.Counted.Count(t => t.ApprovalKind != "auto"),
            facts.Sessions.Count(s => s.PlanPublished && s.PrimaryTickets == 0),
            facts.Sessions.Sum(s => s.ManualPlanRuns),
            facts.P7Verdict is null ? "unavailable: " + facts.P7Label : facts.P7Verdict + (facts.P7Label.Length == 0 ? string.Empty : " (" + facts.P7Label + ")"),
            facts.Exit?.ChargeSheetConfirmed ?? false,
            facts.Services,
            facts.Switches,
            blockers,
            NotJudged);

    private static string Signed(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}

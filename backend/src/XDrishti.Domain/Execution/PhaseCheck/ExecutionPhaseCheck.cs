using System.Globalization;

namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>
/// The P12 phase check (spec P12-12, ADR 0169): 30 consecutive counted semi-auto sessions with zero reconciliation breaks, judged per account from stored facts, pure. MET only with the evidence (never on fewer than the need, never with a malformed window),
/// NOT MET only on a stored broken session inside the window, NOT EVALUABLE otherwise. The phase exit is MET when at least one account is MET. An unavailable or missing fact never counts as clean.
/// </summary>
public static class ExecutionPhaseCheck
{
    public const int RequiredSessions = 30;

    public const string SemiAuto = "semi_auto";

    private const int SlippageMinFills = 30;

    public static ExecutionPhaseReport Evaluate(ExecutionPhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var accounts = facts.Accounts.Select(a => Account(a, facts.Needed)).ToList();
        string exit;
        string reason;
        if (accounts.Any(a => a.Verdict == PhaseVerdicts.Met))
        {
            exit = PhaseVerdicts.Met;
            reason = "at least one account has completed the run: " + string.Join(", ", accounts.Where(a => a.Verdict == PhaseVerdicts.Met).Select(a => OneLine(a.Name)));
        }
        else if (accounts.Any(a => a.Verdict == PhaseVerdicts.NotMet))
        {
            exit = PhaseVerdicts.NotMet;
            reason = "no account has completed the run and a broken session is on record: " + string.Join(", ", accounts.Where(a => a.Verdict == PhaseVerdicts.NotMet).Select(a => OneLine(a.Name)));
        }
        else
        {
            exit = PhaseVerdicts.NotEvaluable;
            reason = accounts.Count == 0 ? "no account has a Trading role, so there is no semi-auto window" : "no account has a semi-auto window with a verdict yet";
        }

        var criterion = string.Create(CultureInfo.InvariantCulture, $"{facts.Needed} semi-auto sessions with zero reconciliation breaks: {exit} ({reason})");
        return new ExecutionPhaseReport(facts.Through, exit, criterion, facts.Needed, accounts, Boundary(facts.Boundary), ExecutionPhaseLimits.Limits);
    }

    private static AccountPhaseReport Account(AccountPhaseFacts account, int needed)
    {
        var verdicts = account.Sessions.Select(SessionClassifier.Classify).ToList();
        var streak = AccountStreak.Compute(verdicts, needed);
        var sandbox = verdicts.Count(v => v.Class != SessionClass.Broken && v.Facts.LiveEntries == 0 && v.Facts.SandboxIntents > 0);
        string verdict;
        string label;
        if (account.WindowStart is null)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            label = "NOT EVALUABLE (no semi-auto window" + (sandbox > 0 ? "; " + Sandbox(sandbox) : string.Empty) + ")";
        }
        else if (account.Mode != SemiAuto)
        {
            verdict = PhaseVerdicts.NotEvaluable;
            label = $"NOT EVALUABLE (the account is in {OneLine(account.Mode)}, not semi_auto, at the end of the window)";
        }
        else if (streak.Streak >= needed)
        {
            if (streak.Malformed)
            {
                verdict = PhaseVerdicts.NotEvaluable;
                label = "NOT EVALUABLE (malformed window)";
            }
            else
            {
                verdict = PhaseVerdicts.Met;
                label = MetLabel(streak);
            }
        }
        else if (streak.Broken > 0)
        {
            verdict = PhaseVerdicts.NotMet;
            var last = verdicts.Last(v => v.Class == SessionClass.Broken);
            label = string.Create(CultureInfo.InvariantCulture, $"NOT MET (streak {streak.Streak} of {needed}; broken session on {Date(last.Facts.Date)}: {string.Join(", ", last.Reasons)}; {Plural(streak.Broken, "broken session")} in the window)");
        }
        else
        {
            verdict = PhaseVerdicts.NotEvaluable;
            var more = needed - streak.Streak;
            label = string.Create(CultureInfo.InvariantCulture, $"NOT EVALUABLE ({streak.Streak} of {needed}, {more} more needed") + (sandbox > 0 ? "; " + Sandbox(sandbox) : string.Empty) + ")";
        }

        var lastBreak = streak.Broken == 0 ? null : Date(streak.BrokenDates[^1]) + " " + string.Join(", ", streak.LastBreakReasons);
        return new AccountPhaseReport(
            account.AccountId, account.Name, account.WindowStart, account.Mode, verdict, streak.Streak, streak.Counted, streak.Idle, streak.Broken, streak.OutOfMode, sandbox, streak.FirstMetAt, lastBreak,
            [.. verdicts.Select(Line)], Facts(account), label);
    }

    private static string MetLabel(AccountStreak streak)
    {
        var parts = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{Plural(streak.Streak, "counted session")} since {Date(streak.StreakStart!.Value)}") };
        if (streak.Broken > 0)
        {
            parts.Add(Plural(streak.Broken, "broken session") + " earlier in the window, " + JoinAnd(streak.BrokenDates.Select(Date)));
        }

        if (streak.Idle > 0)
        {
            parts.Add(Plural(streak.Idle, "idle session"));
        }

        if (streak.OutOfMode > 0)
        {
            parts.Add(Plural(streak.OutOfMode, "session") + " out of mode");
        }

        return "MET (" + string.Join("; ", parts) + ")";
    }

    private static SessionLine Line(SessionVerdict verdict)
    {
        var f = verdict.Facts;
        var median = f.Tickets.MedianMinutes;
        return new SessionLine(
            f.Date, SessionClasses.Word(verdict.Class), verdict.Reasons, f.LiveEntries, f.SandboxIntents, f.Tickets, Run(f.Eod), Run(f.Final), f.BreakKinds.Count, [.. f.BreakKinds.Distinct().Order(StringComparer.Ordinal)],
            [.. f.Alarms.Order(StringComparer.Ordinal)], [.. f.Halts.Order(StringComparer.Ordinal)], f.DayState ?? "none", median, median is null ? "insufficient" : "value");
    }

    private static string Run(RunFact? run) => run is null ? "missing" : run.Readable ? "readable" : "unreadable";

    private static List<PhaseFact> Facts(AccountPhaseFacts account)
    {
        var x = account.Extras;
        var facts = new List<PhaseFact>
        {
            new("protection_coverage", "unavailable", "no stored source for a confirmed broker-side stop in this build"),
            x.DrillsInWindow == 0
                ? new PhaseFact("kill_switch_drills", "empty", "no drill in the window")
                : new PhaseFact("kill_switch_drills", "value", string.Create(CultureInfo.InvariantCulture, $"{x.DrillsInWindow} in the window, last {Date(x.LastDrillDate!.Value)} {(x.LastDrillPassed == true ? "passed" : "failed")}")),
            account.Sessions.Count == 0
                ? new PhaseFact("not_ready_days", "empty", "no session in the window")
                : new PhaseFact("not_ready_days", "value", string.Create(CultureInfo.InvariantCulture, $"{x.NotReadyDays} of {account.Sessions.Count} sessions")),
            new("max_sends_per_second", "unavailable", "no stored source in this build"),
            new("demotions", "value", account.Demotions.Count == 0 ? "0" : string.Create(CultureInfo.InvariantCulture, $"{account.Demotions.Count}: ") + string.Join(", ", account.Demotions.Select(Date))),
            new("double_submits_refused", "unavailable", "no stored source in this build"),
            AlgoId(x),
            new("foreign_orders", "value", string.Create(CultureInfo.InvariantCulture, $"{x.ForeignBreaks} foreign or unknown-order breaks listed in the sessions")),
            Slippage(x),
            new("canary", x.CanaryC1 is null || x.CanaryC2 is null ? "unavailable" : "value", "C1 " + (x.CanaryC1 is { } c1 ? Date(c1) : "not recorded") + ", C2 " + (x.CanaryC2 is { } c2 ? Date(c2) : "not recorded")),
        };
        if (account.SessionsTruncated)
        {
            facts.Add(new PhaseFact("read_cap", "value", "the window holds more sessions than the read cap of 200; the oldest are not read"));
        }

        return facts;
    }

    private static PhaseFact AlgoId(AccountExtras x) =>
        x.LiveLegs is null || x.LiveLegsWithAlgoId is null ? new PhaseFact("algo_id", "unavailable", "legs could not be read")
        : x.LiveLegs == 0 ? new PhaseFact("algo_id", "empty", "no live leg")
        : new PhaseFact("algo_id", "value", string.Create(CultureInfo.InvariantCulture, $"{x.LiveLegsWithAlgoId} of {x.LiveLegs} live legs"));

    private static PhaseFact Slippage(AccountExtras x) =>
        x.LiveEntryFills is not { } n ? new PhaseFact("slippage", "unavailable", "fills could not be read")
        : n < SlippageMinFills ? new PhaseFact("slippage", "insufficient", string.Create(CultureInfo.InvariantCulture, $"{n} live entry fills, insufficient below {SlippageMinFills}; the report is P11-03's"))
        : new PhaseFact("slippage", "value", string.Create(CultureInfo.InvariantCulture, $"{n} live entry fills; the report is P11-03's"));

    private static PhaseFact Boundary(BoundaryFacts b)
    {
        var same = b.Current.SequenceEqual(b.Pinned, StringComparer.Ordinal);
        if (same && b.ProbesAllowed.Count == 0)
        {
            return new PhaseFact("boundary", "value", string.Create(CultureInfo.InvariantCulture, $"MET: the data-side allow-list holds its {b.Pinned.Count} pinned entries and declares no order endpoint"));
        }

        var extra = b.Current.Except(b.Pinned, StringComparer.Ordinal).Concat(b.ProbesAllowed.Select(p => "allowed probe " + p));
        return new PhaseFact("boundary", "value", "NOT MET: the allow-list differs from its pin or allows an order probe: " + string.Join("; ", extra));
    }

    internal static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Sandbox(int n) => string.Create(CultureInfo.InvariantCulture, $"sandbox sessions are not counted: {n}");

    private static string Plural(int n, string noun) => string.Create(CultureInfo.InvariantCulture, $"{n} {noun}") + (n == 1 ? string.Empty : "s");

    private static string JoinAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }
}

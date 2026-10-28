using System.Globalization;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The small-live phase check (spec P11-06, ADR 0153): eight criteria and the integrity line judged from stored facts, pure. A criterion is NOT MET only on a stored fact against it; shortness or a missing source is NOT EVALUABLE; MET needs the evidence.
/// Overall MET only when all nine are MET and the window passes the malformed-window guard. Never recommends an action beyond a sentence.
/// </summary>
public static class LivePhaseCheck
{
    private const double Eps = 1e-9;

    public static LivePhaseReport Evaluate(LivePhaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var sessions = facts.Sessions;
        var elapsed = facts.WindowStart is { } start ? facts.Through.DayNumber - start.DayNumber + 1 : 0;
        var criteria = new List<LiveCriterion>
        {
            Duration(facts, elapsed),
            RiskBand(facts),
            Volume(facts),
            Within(facts),
            Slippage(facts),
            Discipline(facts),
            Feedback(facts),
            Compliance(facts, elapsed),
            Integrity(facts),
        };

        var overall = criteria.Any(c => c.Verdict == LiveVerdicts.NotMet) ? LiveVerdicts.NotMet
            : criteria.All(c => c.Verdict == LiveVerdicts.Met) ? LiveVerdicts.Met
            : LiveVerdicts.NotEvaluable;
        string label = overall;
        if (overall == LiveVerdicts.Met)
        {
            if (Malformed(facts, elapsed))
            {
                overall = LiveVerdicts.NotEvaluable;
                label = "NOT EVALUABLE (malformed window)";
            }
            else
            {
                label = "MET (" + string.Join("; ", Caveats(facts)) + ")";
            }
        }

        var blockers = Blockers(criteria, facts);
        var recommendation = overall == LiveVerdicts.Met
            ? "Evidence supports proposing step S3 (100 %). Recording it is your act: Ramp tab, Exit confirmed."
            : "Not met: " + (blockers.Count == 0 ? "nothing is recorded yet." : string.Join(" ", blockers.Take(3)));
        return new LivePhaseReport(
            facts.Through, facts.WindowStart, elapsed, sessions.Count, overall, label, criteria, Weeks(sessions), facts.Printed, facts.Setup, blockers, recommendation, LivePhaseLimits.Limits);
    }

    private static bool Malformed(LivePhaseFacts f, int elapsed)
    {
        var ascending = f.Sessions.Zip(f.Sessions.Skip(1), (a, b) => a.Date < b.Date).All(x => x);
        return elapsed < LivePhaseLimits.MinDays || f.Sessions.Count < LivePhaseLimits.MinSessions || !ascending || f.Pairs is not { PairsDistinct: true } || !f.OneLiveAccount || !f.ThroughIsTradingSession
            || (f.Sessions.Count > 0 && f.Sessions[^1].Date > f.Through);
    }

    private static List<string> Caveats(LivePhaseFacts f)
    {
        var p = f.Pairs!;
        var list = new List<string> { "point estimate", $"one-sided bound {Num(p.UpperOneSided95 ?? 0, 3)} R from {p.Pairs.ToString(CultureInfo.InvariantCulture)} pairs" };
        if (p.LeftOut > 0)
        {
            list.Add($"{p.LeftOut.ToString(CultureInfo.InvariantCulture)} tickets left out of the pairing");
        }

        if (f.OrderEvidenceMissingSessions > 0)
        {
            list.Add($"order evidence missing on {f.OrderEvidenceMissingSessions.ToString(CultureInfo.InvariantCulture)} sessions");
        }

        if (f.StepDownsOrHolds > 0)
        {
            list.Add($"{f.StepDownsOrHolds.ToString(CultureInfo.InvariantCulture)} step-downs or holds in the window, all cleared");
        }

        if (p.PaperIncompleteSessions > 0)
        {
            list.Add($"paper shadow incomplete on {p.PaperIncompleteSessions.ToString(CultureInfo.InvariantCulture)} sessions");
        }

        if (!p.ChargesConfirmed)
        {
            list.Add("charges unconfirmed: gross comparison only");
        }

        if (f.Sessions.Count > 0 && f.Sessions.All(s => s.Step <= 1))
        {
            list.Add("all live trades at S1");
        }

        return list;
    }

    private static LiveCriterion Duration(LivePhaseFacts f, int elapsed)
    {
        const string name = "Duration and coverage";
        if (f.WindowStart is null || f.Sessions.Count == 0)
        {
            return Of("C1", name, LiveVerdicts.NotEvaluable, "no live session yet: nothing to check");
        }

        var week = Math.Min(8, ((elapsed - 1) / 7) + 1);
        if (elapsed < LivePhaseLimits.MinDays || f.Sessions.Count < LivePhaseLimits.MinSessions)
        {
            return Of("C1", name, LiveVerdicts.NotEvaluable, $"week {I(week)} of 8: {I(elapsed)} of {I(LivePhaseLimits.MinDays)} days, {I(f.Sessions.Count)} of {I(LivePhaseLimits.MinSessions)} sessions");
        }

        var eligible = f.Sessions.Where(s => s.Tickets > 0 || s.Fills > 0).ToList();
        if (eligible.Count == 0)
        {
            return Of("C1", name, LiveVerdicts.NotEvaluable, "no session with a live ticket or fill");
        }

        var missing = eligible.Where(s => s.Capture != CaptureHealth.Healthy).Select(s => s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        var coverage = (double)(eligible.Count - missing.Count) / eligible.Count;
        var detail = $"{I(elapsed)} days, {I(f.Sessions.Count)} sessions; capture complete on {Num(coverage, 3)} of {I(eligible.Count)} sessions with a ticket or fill";
        return coverage < LivePhaseLimits.MinCoverage
            ? Of("C1", name, LiveVerdicts.NotMet, detail + "; missing: " + string.Join(", ", missing))
            : Of("C1", name, LiveVerdicts.Met, detail);
    }

    private static LiveCriterion RiskBand(LivePhaseFacts f)
    {
        const string name = "Risk band";
        var withAlloc = f.Sessions.Where(s => s.Scales.Count > 0).ToList();
        var max = withAlloc.Count == 0 ? 0 : withAlloc.Max(s => s.Scales.Max());
        if (max > LivePhaseLimits.ScaleMax + Eps)
        {
            return Of("C2", name, LiveVerdicts.NotMet, $"a live allocation has risk scale {Num(max, 3)}, above {Num(LivePhaseLimits.ScaleMax, 3)}");
        }

        var above = withAlloc.Where(s => s.StepFactor is { } factor && s.Scales.Any(x => x > factor + Eps)).Select(s => s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        if (above.Count > 0)
        {
            return Of("C2", name, LiveVerdicts.NotMet, $"{I(above.Count)} sessions ran above the step factor in force: {string.Join(", ", above)}");
        }

        if (withAlloc.Count < LivePhaseLimits.MinRiskSessions)
        {
            return Of("C2", name, LiveVerdicts.NotEvaluable, $"{I(withAlloc.Count)} of {I(LivePhaseLimits.MinRiskSessions)} sessions carry a live allocation");
        }

        if (withAlloc.Any(s => s.StepFactor is null) || withAlloc.Any(s => s.Scales.Any(x => x < LivePhaseLimits.ScaleMin - Eps)))
        {
            return Of("C2", name, LiveVerdicts.NotEvaluable, "a session has no known step factor, or a scale below the band");
        }

        return Of("C2", name, LiveVerdicts.Met, $"{I(withAlloc.Count)} sessions, every live allocation between {Num(LivePhaseLimits.ScaleMin, 3)} and {Num(LivePhaseLimits.ScaleMax, 3)} and within the step factor in force");
    }

    private static LiveCriterion Volume(LivePhaseFacts f)
    {
        const string name = "Volume";
        if (f.Pairs is null)
        {
            return Of("C3", name, LiveVerdicts.NotEvaluable, $"{I(f.GradedRoundTrips)} of {I(LivePhaseLimits.MinRoundTrips)} round trips; the paper record could not be read, so pairs are unknown");
        }

        if (f.GradedRoundTrips >= LivePhaseLimits.MinRoundTrips && f.Pairs.Pairs >= LivePhaseLimits.MinPairs)
        {
            return Of("C3", name, LiveVerdicts.Met, $"{I(f.GradedRoundTrips)} round trips, {I(f.Pairs.Pairs)} paired tickets");
        }

        var days = Math.Max(1, f.Sessions.Count);
        var rate = (double)f.GradedRoundTrips / days;
        var eta = rate > 0 ? $", about {I((int)Math.Ceiling(Math.Max(LivePhaseLimits.MinRoundTrips - f.GradedRoundTrips, 0) / rate))} more sessions at {Num(rate, 2)} a session" : ", no pace yet";
        return Of("C3", name, LiveVerdicts.NotEvaluable, $"{I(f.GradedRoundTrips)} of {I(LivePhaseLimits.MinRoundTrips)}, {I(f.Pairs.Pairs)} of {I(LivePhaseLimits.MinPairs)}{eta}");
    }

    private static LiveCriterion Within(LivePhaseFacts f)
    {
        const string name = "Within expectations";
        if (f.Pairs is not { } p)
        {
            return Of("C4", name, LiveVerdicts.NotEvaluable, "the paper record could not be read");
        }

        var interval = p.Mean is { } m && p.Lower95 is { } lo && p.Upper95 is { } hi ? $"mean {Num(m, 4)} R, 95 % interval {Num(lo, 4)} to {Num(hi, 4)}" : "no interval";
        var detail = $"{I(p.Pairs)} pairs, verdict {p.Verdict}; {interval}";
        return p.Verdict switch
        {
            NonInferiority.Within => Of("C4", name, LiveVerdicts.Met, detail),
            NonInferiority.NotWithin => Of("C4", name, LiveVerdicts.NotMet, detail),
            _ => Of("C4", name, LiveVerdicts.NotEvaluable, detail),
        };
    }

    private static LiveCriterion Slippage(LivePhaseFacts f)
    {
        const string name = "Slippage";
        var n = f.SlippageR.Count;
        if (n < LivePhaseLimits.MinSlippageN || f.SlippageKind is null || OneSidedT.Bounds(f.SlippageR) is not { } b)
        {
            return Of("C5", name, LiveVerdicts.NotEvaluable, $"{I(n)} of {I(LivePhaseLimits.MinSlippageN)} measured entries of one reference kind");
        }

        var detail = $"{I(n)} entries ({f.SlippageKind}), mean {Num(b.Mean, 4)} R, one-sided 95 % bounds {Num(b.Lower, 4)} to {Num(b.Upper, 4)}";
        return Math.Round(b.Upper, 9) <= LivePhaseLimits.SlippageUpperR ? Of("C5", name, LiveVerdicts.Met, detail)
            : Math.Round(b.Lower, 9) > LivePhaseLimits.SlippageUpperR ? Of("C5", name, LiveVerdicts.NotMet, detail)
            : Of("C5", name, LiveVerdicts.NotEvaluable, detail + " (not demonstrated)");
    }

    private static LiveCriterion Discipline(LivePhaseFacts f)
    {
        const string name = "Discipline and breaches";
        var recentStepDown = f.StepDownSessionsAgo.Count(a => a < LivePhaseLimits.StepDownSessions);
        if (f.HoldActive || recentStepDown > 0 || f.AfterHaltUncleared > 0)
        {
            var why = new List<string>();
            if (f.HoldActive)
            {
                why.Add("a hold is active");
            }

            if (recentStepDown > 0)
            {
                why.Add($"{I(recentStepDown)} step-down(s) in the last {I(LivePhaseLimits.StepDownSessions)} sessions");
            }

            if (f.AfterHaltUncleared > 0)
            {
                why.Add($"{I(f.AfterHaltUncleared)} after-halt fill(s) with no later clear");
            }

            return Of("C6", name, LiveVerdicts.NotMet, string.Join("; ", why));
        }

        if (f.IntradayEntries > 0 && (double)f.OrderEvidenceUnavailable / f.IntradayEntries > LivePhaseLimits.MaxEvidenceMissingShare)
        {
            return Of("C6", name, LiveVerdicts.NotEvaluable, $"protection or order evidence unavailable for {I(f.OrderEvidenceUnavailable)} of {I(f.IntradayEntries)} intraday entries");
        }

        return Of("C6", name, LiveVerdicts.Met, $"no hold, no recent step-down, no uncleared after-halt fill; {I(f.IntradayEntries)} intraday entries");
    }

    private static LiveCriterion Feedback(LivePhaseFacts f)
    {
        const string name = "Execution feedback delivered";
        var cap = f.CapacityState ?? "unreadable";
        var detail = $"weekly estimates ran in {I(f.WeeksWithEstimates)} of the last {I(LivePhaseLimits.EstimateWeeks)} weeks; capacity score {cap}";
        var delivered = f.WeeksWithEstimates >= LivePhaseLimits.EstimateWeeks && f.CapacityState is "value" or "insufficient";
        return delivered
            ? Of("C7", name, LiveVerdicts.Met, detail)
            : Of("C7", name, LiveVerdicts.NotEvaluable, detail);
    }

    private static LiveCriterion Compliance(LivePhaseFacts f, int elapsed)
    {
        const string name = "Compliance";
        if (f.WindowStart is not { } start)
        {
            return Of("C8", name, LiveVerdicts.NotEvaluable, "no live session yet");
        }

        var bad = new List<string>();
        foreach (var kind in ComplianceKinds.All)
        {
            var rec = f.Compliance.FirstOrDefault(c => c.Kind == kind);
            if (rec?.DecidedOn is not { } decided || rec.NextReviewOn is not { } next)
            {
                bad.Add(ComplianceKinds.Label(kind) + ": not done");
            }
            else if (decided < start || next <= f.Through)
            {
                bad.Add(ComplianceKinds.Label(kind) + (decided < start ? ": decided before the window" : ": review overdue"));
            }
        }

        if (bad.Count == 0)
        {
            return Of("C8", name, LiveVerdicts.Met, "static IP, Dhan IP rule for reads and SEBI re-check are recorded in the window and not overdue");
        }

        return Of("C8", name, elapsed < LivePhaseLimits.MinDays ? LiveVerdicts.NotEvaluable : LiveVerdicts.NotMet, string.Join("; ", bad));
    }

    private static LiveCriterion Integrity(LivePhaseFacts f)
    {
        const string name = "Capture integrity";
        if (!f.CaptureRan)
        {
            return Of("I", name, LiveVerdicts.NotEvaluable, "no capture run");
        }

        var line = f.AllowList is null ? "allow-list: not read" : (f.AllowList.Intact ? "allow-list: MET (" : "allow-list: NOT MET (") + f.AllowList.Detail + ")";
        if (f.Disagreements > 0 || f.UnknownOutcomesOld > 0 || f.DoubleJournaled > 0 || f.AllowList is { Intact: false })
        {
            return Of("I", name, LiveVerdicts.NotMet, $"{I(f.Disagreements)} history disagreements, {I(f.UnknownOutcomesOld)} outcomes unknown after {I(LivePhaseLimits.UnknownGraceSessions)} sessions, {I(f.DoubleJournaled)} fills journaled twice; {line}");
        }

        return f.AllowList is null
            ? Of("I", name, LiveVerdicts.NotEvaluable, line)
            : Of("I", name, LiveVerdicts.Met, $"no disagreement, no old unknown outcome, no fill journaled twice; {line}");
    }

    private static List<LiveWeekLine> Weeks(IReadOnlyList<LiveSessionFact> sessions)
    {
        var cumulative = 0;
        var weeks = new List<LiveWeekLine>();
        foreach (var g in sessions.GroupBy(s => s.Date.AddDays(-(((int)s.Date.DayOfWeek + 6) % 7))).OrderBy(g => g.Key))
        {
            var rows = g.OrderBy(s => s.Date).ToList();
            cumulative += rows.Sum(s => s.RoundTrips);
            var outcomes = rows.SelectMany(s => s.Outcomes).GroupBy(o => o.Key, StringComparer.Ordinal).OrderBy(o => o.Key, StringComparer.Ordinal).ToDictionary(o => o.Key, o => o.Sum(x => x.Value), StringComparer.Ordinal);
            var scales = rows.SelectMany(s => s.Scales).ToList();
            weeks.Add(new LiveWeekLine(g.Key, rows.Count, rows.Sum(s => s.Tickets), outcomes, rows.Sum(s => s.RoundTrips), cumulative, rows[^1].Step, scales.Count == 0 ? null : scales.Max(),
                rows.Count(s => s.Capture == CaptureHealth.Healthy), rows.Sum(s => s.Halts), rows.Sum(s => s.Holds), rows.Sum(s => s.Breaches)));
        }

        return weeks;
    }

    private static List<string> Blockers(IReadOnlyList<LiveCriterion> criteria, LivePhaseFacts f)
    {
        var list = new List<string>();
        foreach (var c in criteria.Where(c => c.Verdict != LiveVerdicts.Met))
        {
            list.Add($"{c.Code} {c.Name}: {c.Verdict} - {c.Detail} ({Act(c)})");
        }

        foreach (var s in f.Setup.Where(s => !s.On && s.Blocks))
        {
            list.Add($"{s.Kind} {s.Name} is off (your act)");
        }

        return list;
    }

    private static string Act(LiveCriterion c) => c.Code switch
    {
        "C1" or "C3" or "C4" or "C5" => c.Verdict == LiveVerdicts.NotMet ? "your decision" : "time",
        "C2" or "C6" => "your act: Ramp tab",
        "C7" => "your act: switch the live-estimates service on",
        "C8" => "your act: Compliance tab",
        _ => "your act: look at the Capture tab",
    };

    private static LiveCriterion Of(string code, string name, string verdict, string detail) => new(code, name, verdict, detail);

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Num(double v, int digits) => v.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);
}

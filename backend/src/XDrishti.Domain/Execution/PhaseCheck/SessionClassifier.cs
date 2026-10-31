namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>Classifies a session (spec P12-12 criterion 1). Pure; an unavailable or missing fact never makes a day clean.</summary>
public static class SessionClassifier
{
    public static readonly IReadOnlyList<string> BreakingAlarms = ["protection_breach", "flatten_started", "oversold"];

    public static readonly IReadOnlyList<string> BreakingHalts = ["reconciliation_break", "protection_breach", "oversold"];

    public static SessionVerdict Classify(SessionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var outside = new List<string>();
        if (!facts.InModeAtOpen)
        {
            outside.Add("not_semi_auto_at_open");
        }

        if (facts.ModeChangedInSession)
        {
            outside.Add("mode_changed_in_session");
        }

        if (facts.InModeAtOpen && facts.CanaryPending)
        {
            outside.Add("canary_session");
        }

        if (outside.Count > 0)
        {
            return new SessionVerdict(facts, SessionClass.OutOfMode, outside);
        }

        var broken = new List<string>();
        broken.AddRange(facts.BreakKinds.Select(k => "break:" + k));
        if (facts.LiveEntries > 0 || facts.PositionCarriedIn)
        {
            AddRun(broken, "eod", facts.Eod);
            AddRun(broken, "final", facts.Final);
        }

        broken.AddRange(facts.Alarms.Where(a => BreakingAlarms.Contains(a)).Distinct().Select(a => "alarm:" + a));
        broken.AddRange(facts.Halts.Where(h => BreakingHalts.Contains(h)).Distinct().Select(h => "halt:" + h));
        if (facts.UnconfirmedEntries > 0)
        {
            broken.Add("unconfirmed_entry");
        }

        if (broken.Count > 0)
        {
            return new SessionVerdict(facts, SessionClass.Broken, broken);
        }

        return facts.LiveEntries - facts.UnconfirmedEntries >= 1
            ? new SessionVerdict(facts, SessionClass.Counted, [])
            : new SessionVerdict(facts, SessionClass.Idle, []);
    }

    private static void AddRun(List<string> reasons, string name, RunFact? run)
    {
        if (run is null)
        {
            reasons.Add(name + "_missing");
        }
        else if (!run.Readable)
        {
            reasons.Add(name + "_unreadable");
        }
    }
}

using System.Globalization;

namespace XDrishti.Domain.Planning;

/// <summary>The three calls of the phase check, as printed (spec P7-18).</summary>
public static class PlanPhaseVerdicts
{
    /// <summary>The stored records show the criterion is satisfied, with enough sessions and the evidence it needs.</summary>
    public const string Met = "MET";

    /// <summary>The stored records show a fact against the criterion.</summary>
    public const string NotMet = "NOT MET";

    /// <summary>The records are missing, too few, or the criterion is proved by tests that this check does not run: no call is made.</summary>
    public const string NotEvaluable = "NOT EVALUABLE";
}

/// <summary>One acceptance criterion of P7-00 with the call and the numbers behind it.</summary>
/// <param name="Number">1 to 12, as numbered in the P7-00 spec.</param>
/// <param name="Name">The criterion in the spec's words.</param>
/// <param name="Verdict">MET, NOT MET or NOT EVALUABLE.</param>
/// <param name="Detail">The numbers and the reason.</param>
/// <param name="Caveat">What a MET rests on without being verified here, printed beside the label; null when none.</param>
public sealed record PlanPhaseCriterion(int Number, string Name, string Verdict, string Detail, string? Caveat = null);

/// <summary>One planned session of the window.</summary>
/// <param name="Date">The planned session.</param>
/// <param name="State"><c>counted</c>, <c>manual_run</c>, <c>failed_closed</c>, <c>no_run</c>, <c>plan_missing</c> or <c>before_history</c>.</param>
/// <param name="ScheduledRuns">Attempts started by the schedule or its catch-up.</param>
/// <param name="ManualRuns">Attempts a person started.</param>
/// <param name="FailedRuns">Attempts that failed closed.</param>
/// <param name="Published">Whether a published row exists.</param>
/// <param name="PlanId">The plan, or null.</param>
/// <param name="Primary">Primary tickets, or null without a plan.</param>
/// <param name="Reserve">Reserve tickets, or null.</param>
/// <param name="Rejected">Rejected candidates, or null.</param>
/// <param name="ZeroPrimary">True when the plan has no primary ticket.</param>
/// <param name="Skeleton">True when the plan is the empty skeleton.</param>
/// <param name="Gates">The gate results in words.</param>
/// <param name="FinishedAt">When the deciding attempt finished, or null.</param>
/// <param name="Note">Why the session does or does not count.</param>
public sealed record PlanPhaseSession(
    DateOnly Date,
    string State,
    int ScheduledRuns,
    int ManualRuns,
    int FailedRuns,
    bool Published,
    long? PlanId,
    int? Primary,
    int? Reserve,
    int? Rejected,
    bool ZeroPrimary,
    bool Skeleton,
    string Gates,
    DateTimeOffset? FinishedAt,
    string Note);

/// <summary>The P7 phase check: the acceptance criteria, the ten sessions, the setup and the limits that are always printed.</summary>
/// <param name="Through">The last planned session of the window.</param>
/// <param name="Streak">Consecutive counted sessions ending at <see cref="Through"/>.</param>
/// <param name="ExitCriterion">The call on the roadmap exit (criterion 1): MET only after ten consecutive scheduled published sessions.</param>
/// <param name="Criteria">The criteria in order.</param>
/// <param name="Sessions">The sessions of the window, oldest first.</param>
/// <param name="Services">The services and their state.</param>
/// <param name="Switches">The owner's switches.</param>
/// <param name="Blockers">Which switches and services are still off, and why the run cannot yet happen; empty when none.</param>
/// <param name="Counts">Counts of stored records.</param>
/// <param name="Limits">The honest limits; never empty.</param>
public sealed record PlanPhaseReport(
    DateOnly Through,
    int Streak,
    string ExitCriterion,
    IReadOnlyList<PlanPhaseCriterion> Criteria,
    IReadOnlyList<PlanPhaseSession> Sessions,
    IReadOnlyList<PhaseService> Services,
    IReadOnlyList<PhaseSwitch> Switches,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<PhaseCount> Counts,
    IReadOnlyList<string> Limits)
{
    /// <summary>The report as text. Deterministic: the same facts give the same text. Times are India time.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "phase check P7 (exit criterion of the roadmap: plans published 10 consecutive sessions unattended; ADR 0097; stored records only, nothing is written)",
            string.Create(c, $"window: the {Sessions.Count} trading sessions up to and including {Through:yyyy-MM-dd}; consecutive counted sessions ending there: {Streak}"),
            "exit criterion (1): " + ExitCriterion,
        };
        foreach (var criterion in Criteria)
        {
            lines.Add(string.Create(c, $"{criterion.Number}. {Label(criterion)}  {criterion.Name}"));
            lines.Add("   " + criterion.Detail);
            if (criterion.Number == 1)
            {
                lines.AddRange(Sessions.Select(s => "   " + SessionLine(s)));
            }
        }

        lines.Add(string.Create(c, $"summary: {Criteria.Count(x => x.Verdict == PlanPhaseVerdicts.Met)} met, {Criteria.Count(x => x.Verdict == PlanPhaseVerdicts.NotMet)} not met, {Criteria.Count(x => x.Verdict == PlanPhaseVerdicts.NotEvaluable)} not evaluable"));
        lines.AddRange(Services.Select(s => $"service {s.Id}: {s.State} ({s.Role}{(s.Required ? ", needed for the exit" : string.Empty)})"));
        lines.AddRange(Switches.Select(s => $"switch {s.Name}: {(s.On ? "ON" : "OFF")} ({s.Effect})"));
        lines.AddRange(Blockers.Select(b => "blocker: " + b));
        if (Blockers.Count == 0)
        {
            lines.Add("blocker: none found in the services and switches read");
        }

        lines.AddRange(Counts.Select(n => string.Create(c, $"count {n.Name}: {n.Value}")));
        lines.AddRange(Limits.Select(l => "limit: " + l));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The label of a criterion as printed: its verdict, with the caveat beside a MET that rests on something not verified here.</summary>
    public static string Label(PlanPhaseCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        return criterion.Verdict == PlanPhaseVerdicts.Met && criterion.Caveat is not null ? $"{criterion.Verdict} ({criterion.Caveat})" : criterion.Verdict;
    }

    private static string SessionLine(PlanPhaseSession s)
    {
        var c = CultureInfo.InvariantCulture;
        var plan = s.PlanId is null ? "no plan" : string.Create(c, $"plan {s.PlanId} primary {s.Primary} reserve {s.Reserve} rejected {s.Rejected}{(s.ZeroPrimary ? " ZERO PRIMARY" : string.Empty)}{(s.Skeleton ? " skeleton" : string.Empty)}");
        var finished = s.FinishedAt is { } at ? Market.IndiaTime.ToIst(at).ToString("yyyy-MM-dd HH:mm 'IST'", c) : "-";
        return string.Create(c, $"{s.Date:yyyy-MM-dd}  {s.State}  scheduled {s.ScheduledRuns} manual {s.ManualRuns} failed {s.FailedRuns}  {(s.Published ? "published" : "not published")}  {plan}  gates: {s.Gates}  finished {finished}  {s.Note}");
    }
}

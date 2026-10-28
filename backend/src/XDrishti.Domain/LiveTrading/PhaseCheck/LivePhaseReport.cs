namespace XDrishti.Domain.LiveTrading;

/// <summary>The three calls of the live phase check (spec P11-06): the words are the ones the paper and plan checks print.</summary>
public static class LiveVerdicts
{
    public const string Met = "MET";
    public const string NotMet = "NOT MET";
    public const string NotEvaluable = "NOT EVALUABLE";
}

/// <summary>One criterion with its call, the numbers behind it and the caveat a MET rests on (null when none).</summary>
public sealed record LiveCriterion(string Code, string Name, string Verdict, string Detail, string? Caveat = null);

/// <summary>One ISO week of the window.</summary>
public sealed record LiveWeekLine(
    DateOnly WeekStart,
    int Sessions,
    int Tickets,
    IReadOnlyDictionary<string, int> Outcomes,
    int RoundTrips,
    int CumulativeRoundTrips,
    int Step,
    double? Scale,
    int CaptureComplete,
    int Halts,
    int Holds,
    int Breaches);

/// <summary>The phase check: the call, the criteria, the weeks, what is printed, what blocks and the limits. Recomputed on every read; nothing is stored.</summary>
public sealed record LivePhaseReport(
    DateOnly Through,
    DateOnly? WindowStart,
    int ElapsedDays,
    int Sessions,
    string ExitVerdict,
    string ExitCriterion,
    IReadOnlyList<LiveCriterion> Criteria,
    IReadOnlyList<LiveWeekLine> Weeks,
    IReadOnlyList<LivePrintedLine> Printed,
    IReadOnlyList<LiveSetupItem> Setup,
    IReadOnlyList<string> Blockers,
    string Recommendation,
    IReadOnlyList<string> Limits);

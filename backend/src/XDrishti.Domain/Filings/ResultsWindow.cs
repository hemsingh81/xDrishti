namespace XDrishti.Domain.Filings;

/// <summary>
/// The one window rule of the event-risk filter (P15-06, pure), used by the simulator (<c>not_event_day</c>, N = the side's hold), the plan selection (<c>exclude.results_within_days</c>) and, for the days it hands over, the plan checklist: a candidate is
/// <b>inside</b> iff <c>0 &lt;= days_to_results &lt;= N</c> sessions. An answer that is not a value, or a negative number, is <see cref="WindowOutcome.Unavailable"/>, never a pass.
/// </summary>
public static class ResultsWindow
{
    /// <summary>The default N of <c>exclude.results_within_days</c> (doc 16).</summary>
    public const int DefaultDays = 2;

    /// <summary>The widest N the owner may set.</summary>
    public const int MaxDays = 10;

    public static bool IsValidDays(int days) => days is >= 0 and <= MaxDays;

    /// <summary>The sessions to the next results event as a non-negative number, or null (unavailable, or a value that cannot be).</summary>
    public static int? DaysOrNull(ResultsAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.State == ResultsAnswer.Value && answer.Days is { } days and >= 0 ? days : null;
    }

    /// <summary>The answer against a window of <paramref name="days"/> sessions (0 is the event session alone).</summary>
    public static WindowOutcome Classify(ResultsAnswer answer, int days)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "A window is 0 or more sessions.");
        }

        return DaysOrNull(answer) is { } d ? (d <= days ? WindowOutcome.Inside : WindowOutcome.Outside) : WindowOutcome.Unavailable;
    }

    /// <summary>The value for <c>PlanCandidate.ExcludedByEvent</c>: true inside the window, false outside, null when unavailable.</summary>
    public static bool? ExcludedByEvent(ResultsAnswer answer, int days) => Classify(answer, days) switch
    {
        WindowOutcome.Inside => true,
        WindowOutcome.Outside => false,
        _ => null,
    };
}

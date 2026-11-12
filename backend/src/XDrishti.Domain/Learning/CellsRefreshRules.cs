using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>What the scheduled refresh does with one source run of the newest snapshot (spec LC-01, D2).</summary>
public enum RefreshSourceAction
{
    /// <summary>The run is kept as it is: its window already reaches the refresh end.</summary>
    UpToDate,

    /// <summary>The run is kept as it is: its version has no pre-registration, so it is never extended on its own.</summary>
    NotRegistered,

    /// <summary>The run is simulated again over the same request with only its end moved to the refresh end.</summary>
    Extend,
}

/// <summary>
/// The pure rules of the scheduled cell-statistics refresh (spec LC-01, ADR 0277): the refresh end, what happens to each source, when the bell speaks and the sentence every run message carries. No clock and no I/O.
/// </summary>
public static class CellsRefreshRules
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string ServiceId = "cells-refresh";

    /// <summary>The trial kind of a refresh run in the test ledger (<c>lab.test_ledger.trial_kind</c>).</summary>
    public const string TrialKind = "cells_refresh";

    /// <summary>The bell notice kind.</summary>
    public const string StaleNoticeKind = "cells_stale";

    /// <summary>The default of <c>Learning:CellsRefresh:NoticeAfterSessions</c>.</summary>
    public const int DefaultNoticeAfterSessions = 3;

    /// <summary>
    /// The last date a refreshed run may reach: the earlier of the last closed session and the day before the hold-out start less the training embargo (D3, ADR 0243: the snapshot reaches decisions with no human in between, so the stricter cut applies). Null when no block is fixed:
    /// the refresh then fails closed.
    /// </summary>
    public static DateOnly? RefreshEnd(DateOnly lastClosedSession, DateOnly? holdOutStart, int embargoDays)
    {
        if (holdOutStart is not { } start)
        {
            return null;
        }

        var cut = start.AddDays(-1 - embargoDays);
        return cut < lastClosedSession ? cut : lastClosedSession;
    }

    /// <summary>What happens to a source whose window ends on <paramref name="runTo"/> (D2): a window that reaches the end stays; a version without a registration stays; the rest is simulated again.</summary>
    public static RefreshSourceAction Decide(DateOnly runTo, DateOnly refreshEnd, bool registered) =>
        runTo >= refreshEnd ? RefreshSourceAction.UpToDate : registered ? RefreshSourceAction.Extend : RefreshSourceAction.NotRegistered;

    /// <summary>Whether the bell speaks (D8): the newest as-of date is at least <paramref name="threshold"/> sessions behind the last closed session.</summary>
    public static bool ShouldNotify(int ageSessions, int threshold) => ageSessions >= threshold;

    /// <summary>The text of the bell notice (D8).</summary>
    public static string StaleText(int ageSessions, int gateLimit, string lastRefresh) =>
        string.Create(CultureInfo.InvariantCulture, $"Cell statistics are {ageSessions} sessions old; the nightly plan fails at {gateLimit + 1}. Last refresh: {lastRefresh}.");

    /// <summary>The sentence every run message carries (spec LC-01, "Before HO-01"): a re-stamp of the same trades with a later as-of date shrinks the weights, so the figures drift toward the prior and are not newer evidence.</summary>
    public static string DriftNote(DateOnly? end) =>
        "data through " + (end is { } e ? e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "the refresh end (not determined)") + "; no new trades can enter until a hold-out block is released (HO-01)";
}

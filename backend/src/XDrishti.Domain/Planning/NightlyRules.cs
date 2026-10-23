using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The pure rules of the nightly pipeline (spec P7-08): which session is planned for, the review cutoff, the plan key and the fixed thresholds of the gates. No clock and no I/O: the caller passes the instant.
/// </summary>
public static class NightlyRules
{
    /// <summary>How far ahead the calendar is searched for the next session (a long holiday run fits; nothing found means the calendar cannot say).</summary>
    public const int NextSessionSearchDays = 10;

    /// <summary>The longest a statistics snapshot may lag the planned session, in sessions (the statistics are refreshed weekly, P7-00 question 16).</summary>
    public const int MaxCellStatsAgeSessions = 5;

    /// <summary>The largest share of instruments that may be behind, empty or failing before the plan is aborted (doc 15 section 5: more than a stated share). Fixed until the settings slice (P7-00 question 14).</summary>
    public const double MaxBadShare = 0.05;

    /// <summary>The review cutoff of a session in India time (doc 16 <c>plan_review_cutoff</c>, 09:05). Fixed here until the settings slice.</summary>
    public static readonly TimeOnly ReviewCutoffTime = new(9, 5);

    /// <summary>The first trading day after <paramref name="session"/> within <see cref="NextSessionSearchDays"/>, or null when the calendar shows none.</summary>
    public static DateOnly? NextSession(TradingCalendar calendar, DateOnly session)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var i = 1; i <= NextSessionSearchDays; i++)
        {
            var date = session.AddDays(i);
            if (calendar.IsTradingDay(date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>The instant the owner's review of the plan for <paramref name="forDate"/> ends: 09:05 India time on that day.</summary>
    public static DateTimeOffset ReviewCutoff(DateOnly forDate) =>
        new DateTimeOffset(forDate.ToDateTime(ReviewCutoffTime), IndiaTime.Offset).ToUniversalTime(); // UTC: the database driver stores only offset 0

    /// <summary>The idempotency key of the plan of a session: the same session and builder give the same key, so a second publish stores nothing.</summary>
    public static string PlanKey(DateOnly forDate, string builderId) =>
        string.Create(CultureInfo.InvariantCulture, $"nightly:{forDate:yyyy-MM-dd}:{builderId}");

    /// <summary>The moment the session ended, in India time.</summary>
    public static DateTimeOffset SessionEnd(TradingSession session) =>
        new(session.Date.ToDateTime(TimeOnly.MinValue).AddMinutes(session.ToMinute), IndiaTime.Offset);

    /// <summary>Whether <paramref name="bad"/> of <paramref name="total"/> is a share above <see cref="MaxBadShare"/> (nothing at all counts as too much: an empty universe is not a pass).</summary>
    public static bool TooManyBad(long bad, long total) => total <= 0 || bad > total * MaxBadShare;
}

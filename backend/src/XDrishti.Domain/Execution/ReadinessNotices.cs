namespace XDrishti.Domain.Execution;

/// <summary>One in-app notice (ADR 0027 shape). The id includes the date, so the same notice appears once per day and is gone when its condition is.</summary>
public sealed record ReadinessNotice(string Id, string Kind, string Severity, DateOnly Date);

/// <summary>What the notice schedule reads: the date and whether it is a trading day, the next trading day, whether an account is at assisted or above, whether every such account is ready and whether the token of any of them covers the next session.</summary>
public sealed record ReadinessNoticeFacts(DateOnly Today, bool TodayIsTradingDay, DateOnly NextTradingDay, bool AnyAssisted, bool Ready, bool TokenCoversNextSession);

/// <summary>
/// The reminders of spec P12-11 (criteria 7 and 10) as pure rules, so the derivation in the API (P12-10) and the tests share one schedule. Nothing leaves the machine. Kinds: <c>exec_login_reminder</c> (info), <c>exec_not_ready</c> (warning from 08:45, error from 09:05
/// while not ready), <c>exec_recheck_due</c> (info). Times are India time.
/// </summary>
public static class ReadinessNotices
{
    public const string LoginReminder = "exec_login_reminder";
    public const string NotReady = "exec_not_ready";
    public const string RecheckDue = "exec_recheck_due";

    private static readonly TimeOnly Evening = new(19, 0);
    private static readonly TimeOnly Morning = new(8, 15);
    private static readonly TimeOnly Warn = new(8, 45);
    private static readonly TimeOnly Error = new(9, 5);
    private static readonly TimeOnly SessionEnd = new(16, 30);

    public static IReadOnlyList<ReadinessNotice> Derive(DateTimeOffset now, ReadinessNoticeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var ist = Market.IndiaTime.ToIst(now);
        var today = DateOnly.FromDateTime(ist.DateTime);
        var time = TimeOnly.FromDateTime(ist.DateTime);
        var found = new List<ReadinessNotice>();
        if (!facts.AnyAssisted || today != facts.Today)
        {
            return found;
        }

        if (time >= Evening && facts.NextTradingDay > today && !facts.TokenCoversNextSession)
        {
            found.Add(new($"{LoginReminder}:{facts.NextTradingDay:yyyy-MM-dd}:eve", LoginReminder, "info", facts.NextTradingDay));
        }

        if (facts.TodayIsTradingDay && !facts.Ready && time < SessionEnd)
        {
            if (time >= Morning)
            {
                found.Add(new($"{LoginReminder}:{today:yyyy-MM-dd}:day", LoginReminder, "info", today));
            }

            if (time >= Error)
            {
                found.Add(new($"{NotReady}:{today:yyyy-MM-dd}", NotReady, "error", today));
            }
            else if (time >= Warn)
            {
                found.Add(new($"{NotReady}:{today:yyyy-MM-dd}", NotReady, "warning", today));
            }
        }

        return found;
    }

    /// <summary>Criterion 10: due when there is no sebi_recheck record or today is on or after its next review date, and an account is at assisted or above. This slice stores nothing for it; the record is P11-05's.</summary>
    public static ReadinessNotice? Recheck(DateOnly today, DateOnly? nextReviewOn, bool anyAssisted) =>
        anyAssisted && (nextReviewOn is null || today >= nextReviewOn) ? new($"{RecheckDue}:{today:yyyy-MM-dd}", RecheckDue, "info", today) : null;
}

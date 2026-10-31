namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>
/// The run of counted sessions of one window (spec P12-12 criterion 2): counted adds one, broken sets the streak to zero, idle and out of mode leave it. <see cref="Malformed"/> is true when the sessions are not strictly ascending by date.
/// </summary>
public sealed record AccountStreak(
    int Streak, int Counted, int Idle, int Broken, int OutOfMode, DateOnly? StreakStart, DateOnly? FirstMetAt, IReadOnlyList<DateOnly> BrokenDates, IReadOnlyList<string> LastBreakReasons, bool Malformed)
{
    public static AccountStreak Compute(IReadOnlyList<SessionVerdict> sessions, int needed)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        int streak = 0, counted = 0, idle = 0, outOfMode = 0;
        DateOnly? start = null, firstMet = null;
        var broken = new List<DateOnly>();
        IReadOnlyList<string> lastReasons = [];
        var malformed = false;
        DateOnly? previous = null;
        foreach (var session in sessions)
        {
            var date = session.Facts.Date;
            if (previous is { } p && date <= p)
            {
                malformed = true;
            }

            previous = date;
            switch (session.Class)
            {
                case SessionClass.Counted:
                    counted++;
                    streak++;
                    start ??= date;
                    if (streak >= needed)
                    {
                        firstMet ??= date;
                    }

                    break;
                case SessionClass.Broken:
                    streak = 0;
                    start = null;
                    broken.Add(date);
                    lastReasons = session.Reasons;
                    break;
                case SessionClass.Idle:
                    idle++;
                    break;
                default:
                    outOfMode++;
                    break;
            }
        }

        return new AccountStreak(streak, counted, idle, broken.Count, outOfMode, start, firstMet, broken, lastReasons, malformed);
    }
}

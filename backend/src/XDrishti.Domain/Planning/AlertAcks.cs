namespace XDrishti.Domain.Planning;

/// <summary>Whether a notice is shown.</summary>
public enum AlertVisibility
{
    /// <summary>Counted and shown.</summary>
    Shown,

    /// <summary>Hidden until the snooze ends.</summary>
    Snoozed,

    /// <summary>Hidden for good.</summary>
    Dismissed,
}

/// <summary>The pure rules of mute, snooze and dismiss (spec P7-20, ADR 0115): notification preferences only, they never touch a ticket.</summary>
public static class AlertAcks
{
    /// <summary>The stored action: hide until a time.</summary>
    public const string Snooze = "snooze";

    /// <summary>The stored action: hide for good.</summary>
    public const string Dismiss = "dismiss";

    /// <summary>The stored action: no further crossings of a ticket for its session.</summary>
    public const string Mute = "mute";

    /// <summary>A notice can be snoozed this many times, so a crossing cannot be hidden into irrelevance.</summary>
    public const int MaxSnoozes = 3;

    /// <summary>The snooze lengths in minutes.</summary>
    public static readonly IReadOnlyList<int> SnoozeMinutes = [15, 30, 60];

    /// <summary>The newest act decides; a snooze that has ended shows the notice again (once: it is not hidden again).</summary>
    public static AlertVisibility Visibility(IReadOnlyList<AlertAck> acks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(acks);
        var newest = acks.Count == 0 ? null : acks.MaxBy(a => a.Id);
        return newest switch
        {
            null => AlertVisibility.Shown,
            { Action: Dismiss } => AlertVisibility.Dismissed,
            { Action: Snooze, Until: { } until } when until > now => AlertVisibility.Snoozed,
            _ => AlertVisibility.Shown,
        };
    }

    /// <summary>The end of the snooze that is in force; null when the notice is not snoozed.</summary>
    public static DateTimeOffset? SnoozedUntil(IReadOnlyList<AlertAck> acks, DateTimeOffset now) =>
        Visibility(acks, now) == AlertVisibility.Snoozed ? acks.MaxBy(a => a.Id)?.Until : null;

    /// <summary>True when another snooze is allowed: the notice is not dismissed and fewer than <see cref="MaxSnoozes"/> snoozes exist.</summary>
    public static bool CanSnooze(IReadOnlyList<AlertAck> acks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(acks);
        return Visibility(acks, now) != AlertVisibility.Dismissed && acks.Count(a => a.Action == Snooze) < MaxSnoozes;
    }
}

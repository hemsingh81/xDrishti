namespace XDrishti.Execution.Domain.Limits;

/// <summary>The owner's newest session as the presence rule reads it: when authenticated activity last touched it and whether the owner account is locked now.</summary>
public sealed record OwnerSeen(DateTimeOffset LastSeenAt, bool Locked);

/// <summary>
/// Attendance (spec P12-09 criterion 7, D5): an owner session seen within the attended window by its <c>last_seen_at</c> and not locked. No session, a locked owner or a time in the future of the clock beyond a second is not attended. An open
/// browser tab is not proof of attention (a stated limit). Pure.
/// </summary>
public static class OwnerPresence
{
    public static bool IsAttended(OwnerSeen? seen, DateTimeOffset now, int attendedMinutes) =>
        seen is { Locked: false } s && attendedMinutes > 0 && s.LastSeenAt <= now + TimeSpan.FromSeconds(1) && now - s.LastSeenAt <= TimeSpan.FromMinutes(attendedMinutes);
}

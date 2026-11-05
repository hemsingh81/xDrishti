using XDrishti.Domain.Market;

namespace XDrishti.Domain.Shadow;

/// <summary>The instant after which a session's shadow rows can no longer be written: 09:00 India time of the session (03:30 UTC), before arming (09:10) and the open (09:15). The database repeats the rule (spec CF-08, D2).</summary>
public static class ShadowCutoff
{
    /// <summary>The cut-off time of day in India.</summary>
    public static readonly TimeOnly TimeOfDay = new(9, 0);

    /// <summary>The cut-off instant of <paramref name="session"/>.</summary>
    public static DateTimeOffset For(DateOnly session) => new(session.ToDateTime(TimeOfDay), IndiaTime.Offset);

    /// <summary>Whether a row for <paramref name="session"/> can no longer be written at <paramref name="now"/> (at or after the cut-off).</summary>
    public static bool IsOpened(DateOnly session, DateTimeOffset now) => now >= For(session);
}

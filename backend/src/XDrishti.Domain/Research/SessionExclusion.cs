namespace XDrishti.Domain.Research;

/// <summary>Why a date was not replayed (spec P3-04 Scope 4): the market was closed, the session was a special one, a regular session was too short, or the calendar says there was a session and the instrument has no bar at all. Stored by name; never renamed.</summary>
public enum SessionExclusion
{
    /// <summary>The calendar has no session on the date (a weekend or a holiday that no special session replaces) and the instrument has bars on it.</summary>
    ClosedDay,

    /// <summary>The session is not the regular 09:15 to 15:30 window.</summary>
    SpecialSession,

    /// <summary>A regular session whose window holds fewer than <see cref="ReplayOptions.MinSessionCompleteness"/> of the 375 minutes.</summary>
    ShortSession,

    /// <summary>A trading day of the calendar between the instrument's first and last bar with no bar at all.</summary>
    MissingSession,
}

/// <summary>The stable spelling of <see cref="SessionExclusion"/>.</summary>
public static class SessionExclusions
{
    /// <summary><c>closed_day</c> · <c>special_session</c> · <c>short_session</c> · <c>missing_session</c>.</summary>
    public static string ToCode(this SessionExclusion exclusion) => exclusion switch
    {
        SessionExclusion.ClosedDay => "closed_day",
        SessionExclusion.SpecialSession => "special_session",
        SessionExclusion.ShortSession => "short_session",
        SessionExclusion.MissingSession => "missing_session",
        _ => throw new ArgumentOutOfRangeException(nameof(exclusion), exclusion, "Unknown session exclusion."),
    };
}

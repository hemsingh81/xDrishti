namespace XDrishti.Domain.Research;

/// <summary>How many dates of an instrument ran and how many were excluded, by reason (spec P3-04 Scope 4). The counts are exact even when the notes are cut at 100.</summary>
public sealed record SessionCounts(long Ran, long ClosedDay, long SpecialSession, long ShortSession, long MissingSession)
{
    /// <summary>All zeros: the identity of <see cref="Add"/>.</summary>
    public static SessionCounts Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>The sessions that did not run.</summary>
    public long Excluded => ClosedDay + SpecialSession + ShortSession + MissingSession;

    /// <summary>The field-by-field sum.</summary>
    public SessionCounts Add(SessionCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SessionCounts(Ran + other.Ran, ClosedDay + other.ClosedDay, SpecialSession + other.SpecialSession, ShortSession + other.ShortSession, MissingSession + other.MissingSession);
    }

    /// <summary>The count for one exclusion reason.</summary>
    public long Of(SessionExclusion reason) => reason switch
    {
        SessionExclusion.ClosedDay => ClosedDay,
        SessionExclusion.SpecialSession => SpecialSession,
        SessionExclusion.ShortSession => ShortSession,
        SessionExclusion.MissingSession => MissingSession,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown session exclusion."),
    };
}

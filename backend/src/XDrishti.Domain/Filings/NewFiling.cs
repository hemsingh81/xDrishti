namespace XDrishti.Domain.Filings;

/// <summary>An accepted file ready to be stored (P15-02): the sidecar's words resolved to an instrument. <see cref="BroadcastTs"/> is a UTC instant when <see cref="State"/> is <c>Ok</c>, else null with a closed <see cref="UnavailableReason"/>; a date is never substituted.</summary>
public sealed record NewFiling(
    Guid InstrumentId,
    FilingKind Kind,
    DateOnly? PeriodEnd,
    DateTimeOffset? BroadcastTs,
    BroadcastState State,
    string? UnavailableReason,
    DateTimeOffset IngestedAt,
    string ContentSha256,
    long FileBytes,
    string FileName)
{
    /// <summary>The database keeps microseconds; a sidecar time has up to 100 ns. Every time that is compared or stored is cut to whole microseconds first, so the comparison never sees finer than the store.</summary>
    public static DateTimeOffset? ToMicroseconds(DateTimeOffset? time) => time is { } t ? new DateTimeOffset(t.UtcTicks - (t.UtcTicks % 10), TimeSpan.Zero) : null;

    public Guid Id => FilingId.Create(InstrumentId, Kind, PeriodEnd, ContentSha256);

    /// <summary>Whether a stored envelope of the same bytes says the same about instrument, kind, period and time (the file name and the ingest time do not count). A difference is a sidecar conflict: a broadcast time is never rewritten.</summary>
    public bool SameSidecar(StoredFiling stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.InstrumentId == InstrumentId && stored.Kind == Kind && stored.PeriodEnd == PeriodEnd && stored.BroadcastTs?.UtcDateTime == BroadcastTs?.UtcDateTime;
    }
}

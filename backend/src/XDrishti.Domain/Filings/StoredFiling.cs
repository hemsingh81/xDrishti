namespace XDrishti.Domain.Filings;

/// <summary>One stored envelope (<c>filings.filings</c>). <see cref="BroadcastTs"/> is null exactly when <see cref="State"/> is <c>Unavailable</c>. Insert-only: a row is never changed.</summary>
public sealed record StoredFiling(
    Guid Id,
    Guid InstrumentId,
    FilingKind Kind,
    DateOnly? PeriodEnd,
    DateTimeOffset? BroadcastTs,
    BroadcastState State,
    string? UnavailableReason,
    DateTimeOffset IngestedAt,
    string ContentSha256,
    long FileBytes,
    string FileName,
    Guid? Supersedes);

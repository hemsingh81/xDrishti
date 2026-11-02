using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The insert-only envelope table <c>filings.filings</c> (P15-02, ADR 0180). A row is never updated or deleted. One insert is one transaction.</summary>
public interface IFilingStore
{
    /// <summary>The envelope with these bytes, if one is stored.</summary>
    ValueTask<StoredFiling?> FindByHashAsync(string contentSha256, CancellationToken cancellationToken);

    /// <summary>Stores one envelope in one transaction, applying <c>PointInTimeRule.Decide</c> to the group under a lock so that two hosts storing the same file or two revisions of one period cannot both win. Idempotent on the hash.</summary>
    ValueTask<FilingInsertResult> InsertAsync(NewFiling filing, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IFilingStore.InsertAsync"/> did. <see cref="Existing"/> is the stored envelope of the same bytes (duplicate or conflict).</summary>
public sealed record FilingInsertResult(FilingInsertOutcome Outcome, StoredFiling? Existing = null, Guid? Supersedes = null);

/// <summary>The closed outcomes of an insert.</summary>
public enum FilingInsertOutcome
{
    /// <summary>Stored (with <c>Supersedes</c> when it revises the group's latest filing).</summary>
    Stored,

    /// <summary>Stored unlinked because its time is earlier than the group's latest.</summary>
    StoredOutOfOrder,

    /// <summary>The same bytes and the same sidecar are already stored.</summary>
    Duplicate,

    /// <summary>The same bytes are stored with another symbol, kind, period or time: nothing is changed.</summary>
    SidecarConflict,

    /// <summary>Another file of the group has exactly the same time: not stored.</summary>
    AmbiguousOrder,

    /// <summary>The revision would break the supersede rule: not stored.</summary>
    BadSupersede,
}

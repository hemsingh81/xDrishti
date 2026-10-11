using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>
/// Persistence port for browser sign-ins in progress. New ones are saved by <see cref="IAccountUnitOfWork"/>; using, closing and purging
/// happen at once and atomically (a sign-in must never be used twice, even by two callbacks at the same moment).
/// </summary>
public interface IPendingConnectStore
{
    void Add(PendingConnect pending);

    /// <summary>The newest sign-in of the session that is not used yet (it may have expired), for reading only.</summary>
    ValueTask<PendingConnect?> FindLatestUnusedAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>How many sign-ins were started for the account in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    ValueTask<int> CountStartedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Marks the sign-in used, atomically; false when it was used (or closed) already.</summary>
    ValueTask<bool> TryConsumeAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Closes every unused sign-in of the session (a newer one replaces them).</summary>
    ValueTask CloseUnusedAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Closes every unused sign-in of the account (its client id changed: a sign-in for the old one must not complete).</summary>
    ValueTask CloseUnusedForAccountAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Deletes every sign-in of the account (it was archived).</summary>
    ValueTask DeleteForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Deletes sign-ins started before <paramref name="createdBefore"/>.</summary>
    ValueTask PurgeAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken);
}

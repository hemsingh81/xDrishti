namespace XDrishti.Application.Abstractions.Auth;

/// <summary>Commits everything the auth stores and the audit log have collected, atomically (all or nothing).</summary>
public interface IAuthUnitOfWork
{
    ValueTask SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="SaveChangesAsync"/>, for changes to the owner account (failure counter, lock) that concurrent requests may also make.
    /// Returns false when another request changed the owner account since it was read: nothing was saved and everything that
    /// was collected is discarded, so the caller re-reads the owner, re-applies its change and tries again.
    /// </summary>
    ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Commits everything the account stores, the broker vault and the event log have collected, atomically.</summary>
public interface IAccountUnitOfWork
{
    /// <summary>
    /// Saves all changes in one transaction. Returns false when nothing could be saved because the change collided with a concurrent
    /// change or with a uniqueness rule of the database (primary Data account, slug, name): everything collected
    /// is then discarded.
    /// </summary>
    ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside one database transaction that is serialised against other calls of this method (switching
    /// the primary Data account, deleting a risk profile). The work may save several times; everything is committed only when the returned result is a success,
    /// otherwise rolled back. The work may run again from scratch after a transient database failure, so it must read its own state.
    /// </summary>
    ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken);
}

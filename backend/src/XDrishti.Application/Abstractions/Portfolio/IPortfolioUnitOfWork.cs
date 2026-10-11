using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Saves what the holding store and the account event log have collected, and runs writes that must be all or nothing.</summary>
public interface IPortfolioUnitOfWork
{
    /// <summary>Saves everything collected, atomically (throws when the database refuses).</summary>
    ValueTask SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything collected; false (and the collected changes discarded) when the database refused it because of a rule that a concurrent change
    /// broke (a check or uniqueness violation), for example a lot that was closed after it was read.
    /// </summary>
    ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside one database transaction. The work saves what it collects with <see cref="SaveAsync"/>; everything is
    /// committed only when the returned result is a success, otherwise (or when the work throws) rolled back and the collected changes discarded.
    /// The work may run again from scratch after a transient database failure, so it must read its own state.
    /// </summary>
    ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken);
}

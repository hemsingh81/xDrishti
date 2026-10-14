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
    /// The work may run again from scratch after a transient database failure, and once more after a concurrency conflict (a holding it changed was changed
    /// by someone else since it read it, for example by a quote), so it must read its own state. A second conflict is not thrown: the result is <see cref="Domain.Portfolio.PortfolioErrors.HoldingChanged"/> (everything rolled back).
    /// </summary>
    ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="RunAsync"/>, but a concurrency conflict is not retried: when a holding the work changed was changed by someone else since the work read it (a sync or a quote saved it), everything is rolled back and the result is
    /// <see cref="Domain.Portfolio.PortfolioErrors.HoldingChanged"/>. For work that should rather be skipped than repeated, like the automatic rebuild of lots after a trade sync (ADR 0025).
    /// </summary>
    ValueTask<Result> RunOnceAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken);
}

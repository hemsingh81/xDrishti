using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// Where exit choices are kept (spec P4-06, D10, ADR 0054): <c>research.exit_selections</c> and <c>research.exit_selection_folds</c> behind a port. Insert-only. The selection key is the idempotency key: the same choice again is a no-op, another
/// record under a stored key is a conflict.
/// </summary>
public interface IExitSelectionStore
{
    /// <summary>
    /// Stores the header and the fold rows in one transaction. <c>store.write_failed</c> when the comparison it was made from is not stored (nothing is written); <c>store.run_conflict</c> for another record under a stored key.
    /// </summary>
    ValueTask<Result<StoreOutcome>> StoreAsync(ExitSelectionToStore selection, CancellationToken cancellationToken);
}

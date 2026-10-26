using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// Where finished simulation runs are kept (spec P3-10, ADR 0045): <c>research.sim_runs</c> and <c>research.sim_trades</c> behind a port, so that Application names no database type (ADR 0039). Insert-only: a stored run is never changed. The
/// run key is the idempotency key: the same run again is a no-op, another record under a stored key is a conflict.
/// </summary>
public interface IRunStore
{
    /// <summary>Stores the header and every trade in one transaction. <c>store.run_conflict</c> for another record under a stored key; <c>store.write_failed</c> when the write or its count check failed.</summary>
    ValueTask<Result<StoreOutcome>> StoreAsync(RunToStore run, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the header of a comparison (P4-05, D10): its members are runs stored on their own, and the comparison is refused when one of them is not (<c>store.write_failed</c>). Idempotent by comparison key; another record under a stored key is
    /// <c>store.run_conflict</c>.
    /// </summary>
    ValueTask<Result<StoreOutcome>> StoreComparisonAsync(ComparisonToStore comparison, CancellationToken cancellationToken);

    /// <summary>The header of a stored comparison, or null.</summary>
    ValueTask<StoredComparisonHeader?> FindComparisonAsync(string comparisonKey, CancellationToken cancellationToken);

    /// <summary>The header of a stored run, or null.</summary>
    ValueTask<StoredRunHeader?> FindAsync(string runKey, CancellationToken cancellationToken);

    /// <summary>The stored canonical record text, or null.</summary>
    ValueTask<string?> ReadRecordTextAsync(string runKey, CancellationToken cancellationToken);

    /// <summary>A page of headers, newest first, by cursor <c>(created_at, id)</c>.</summary>
    ValueTask<Result<Page<StoredRunHeader>>> ListAsync(RunListFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>The trades of a run in the record's order, streamed (a damaged row raises <see cref="StoreReadException"/>).</summary>
    IAsyncEnumerable<RunTrade> StreamTradesAsync(string runKey, bool openAtEnd, CancellationToken cancellationToken);

    /// <summary>A page of a run's trades in the record's order by keyset on <c>(open_at_end, ordinal)</c>, with the closed filter.</summary>
    ValueTask<Result<Page<RunTrade>>> PageTradesAsync(string runKey, TradePageFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>One stored trade by its place in the run (legs and costing as <see cref="PageTradesAsync"/> reads them), or null when the run or the trade is not stored (P8-11).</summary>
    ValueTask<RunTrade?> ReadTradeAsync(string runKey, bool openAtEnd, int ordinal, CancellationToken cancellationToken);
}

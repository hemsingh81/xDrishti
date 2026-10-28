namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// The execution feedback record (<c>trading.live_executions</c>, <c>live_order_classes</c>, <c>slippage_estimates</c>, <c>capacity_scores</c>, spec P11-02, ADR 0150): insert-only for the runtime role; a row seen again is not stored again. Nothing here
/// reaches a broker, changes a trading setting, or feeds the simulator.
/// </summary>
public interface ILiveExecutionStore
{
    /// <summary>Stores the executions not stored yet (unique per outcome); returns how many were new.</summary>
    ValueTask<int> InsertExecutionsAsync(IReadOnlyList<NewLiveExecution> rows, CancellationToken cancellationToken);

    /// <summary>The outcome ids of the account and session that already have an execution.</summary>
    ValueTask<IReadOnlySet<long>> ExecutionOutcomeIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    ValueTask<int> InsertOrderClassesAsync(IReadOnlyList<LiveOrderClassRow> rows, CancellationToken cancellationToken);

    /// <summary>The broker order ids of the account and session that already have a class.</summary>
    ValueTask<IReadOnlySet<string>> ClassifiedOrderIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Executions in a range, newest session first, one page.</summary>
    ValueTask<StoredExecutionPage> ListExecutionsAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken);

    ValueTask<StoredLiveExecution?> FindExecutionAsync(long id, CancellationToken cancellationToken);

    /// <summary>Every execution of the sessions up to and including the date (all live accounts), oldest first.</summary>
    ValueTask<IReadOnlyList<StoredLiveExecution>> ExecutionsUntilAsync(DateOnly until, CancellationToken cancellationToken);

    /// <summary>Every order class of the sessions up to and including the date, oldest first.</summary>
    ValueTask<IReadOnlyList<LiveOrderClassRow>> OrderClassesUntilAsync(DateOnly until, CancellationToken cancellationToken);

    /// <summary>The count and the first and last session of the executions up to and including the date (the evidence of an estimate).</summary>
    ValueTask<(int Count, DateOnly? First, DateOnly? Last)> ExecutionSummaryAsync(DateOnly until, CancellationToken cancellationToken);

    ValueTask<int> InsertSlippageEstimatesAsync(IReadOnlyList<NewSlippageEstimate> rows, CancellationToken cancellationToken);

    ValueTask<int> InsertCapacityScoresAsync(IReadOnlyList<NewCapacityScore> rows, CancellationToken cancellationToken);

    /// <summary>The stored snapshot of the week (or the newest week when null); the week is null when nothing is stored.</summary>
    ValueTask<(DateOnly? Week, IReadOnlyList<NewSlippageEstimate> Rows)> SlippageEstimatesAsync(DateOnly? weekStart, CancellationToken cancellationToken);

    ValueTask<(DateOnly? Week, IReadOnlyList<NewCapacityScore> Rows)> CapacityScoresAsync(DateOnly? weekStart, CancellationToken cancellationToken);
}

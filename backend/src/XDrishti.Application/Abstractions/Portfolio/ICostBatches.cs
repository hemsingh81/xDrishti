using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A batch with the figure a list needs: the cost entries of it that still count.</summary>
public sealed record CostBatchRow(CostBatch Batch, int ActiveEntries);

/// <summary>The import batches of depository-charges sheets (<c>portfolio.cost_batches</c>, ADR 0029). Batches are never deleted.</summary>
public interface ICostBatches
{
    /// <summary>One batch, tracked so that undoing it can be saved; null when it does not exist.</summary>
    ValueTask<CostBatch?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One batch for reading with its count; null when it does not exist.</summary>
    ValueTask<CostBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of batches, newest first (ties by id); <paramref name="accountId"/> null for all accounts. Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<CostBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Collects the new batch; <see cref="IPortfolioUnitOfWork"/> saves it.</summary>
    void Add(CostBatch batch);
}

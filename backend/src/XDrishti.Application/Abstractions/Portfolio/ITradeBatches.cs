using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A batch with the figures a list needs: the trades of it that still count and the number of securities among them.</summary>
public sealed record TradeBatchRow(TradeBatch Batch, int ActiveTrades, int Securities);

/// <summary>The import batches of tradebook files (<c>portfolio.trade_batches</c>, ADR 0023). Batches are never deleted.</summary>
public interface ITradeBatches
{
    /// <summary>One batch, tracked so that undoing it can be saved; null when it does not exist.</summary>
    ValueTask<TradeBatch?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One batch for reading with its counts; null when it does not exist.</summary>
    ValueTask<TradeBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of batches, newest first (ties by id); <paramref name="accountId"/> null for all accounts. Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<TradeBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Collects the new batch; <see cref="IPortfolioUnitOfWork"/> saves it.</summary>
    void Add(TradeBatch batch);
}

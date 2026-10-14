using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A batch with the figure a list needs: the dividends of it that still count.</summary>
public sealed record DividendBatchRow(DividendBatch Batch, int ActiveDividends);

/// <summary>The import batches of dividend files (<c>portfolio.dividend_batches</c>, ADR 0028). Batches are never deleted.</summary>
public interface IDividendBatches
{
    /// <summary>One batch, tracked so that undoing it can be saved; null when it does not exist.</summary>
    ValueTask<DividendBatch?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One batch for reading with its count; null when it does not exist.</summary>
    ValueTask<DividendBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of batches, newest first (ties by id); <paramref name="accountId"/> null for all accounts. Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<DividendBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Collects the new batch; <see cref="IPortfolioUnitOfWork"/> saves it.</summary>
    void Add(DividendBatch batch);
}

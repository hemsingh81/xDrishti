using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The dividends the owner entered (<c>portfolio.dividends</c>, ADR 0026). Immutable once stored except for being voided; the runtime role cannot delete them. New dividends and voids are saved by <see cref="IPortfolioUnitOfWork"/>.
/// </summary>
public interface IDividendStore
{
    /// <summary>One dividend, tracked so that voiding it can be saved; null when it does not exist.</summary>
    ValueTask<Dividend?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Collects the new dividend; <see cref="IPortfolioUnitOfWork"/> saves it.</summary>
    void Add(Dividend dividend);

    /// <summary>True when the account already has a dividend that is not voided with the same holding (null: none), pay date and amount. For reading only.</summary>
    ValueTask<bool> ExistsSameAsync(Guid accountId, Guid? holdingId, DateOnly payDate, decimal amount, CancellationToken cancellationToken);

    /// <summary>One page of the dividends of a holding, voided ones included, newest first (pay date, then entered, descending). Total counts all of them.</summary>
    ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForHoldingAsync(Guid holdingId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the dividends of an account (every holding's and the unallocated ones), voided ones included, newest first. Total counts all of them.</summary>
    ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForAccountAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
}

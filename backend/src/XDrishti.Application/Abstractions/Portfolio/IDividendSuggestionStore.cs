using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The suggested dividends (ADR 0026): the account's ledger entries of class <c>dividend</c> that the owner has not decided about yet, and the decisions (<c>portfolio.dividend_suggestions</c>). Decisions are never changed or deleted. New decisions are
/// saved by <see cref="IPortfolioUnitOfWork"/>.
/// </summary>
public interface IDividendSuggestionStore
{
    /// <summary>One page of the account's ledger entries of class dividend without a decision that could be entered as a dividend (see <see cref="DividendRules.CanEnter"/>), newest first (date, then stored order, descending). Total counts all of them.</summary>
    ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListOpenAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One ledger entry for reading only; null when it does not exist.</summary>
    ValueTask<LedgerEntry?> FindEntryAsync(Guid ledgerEntryId, CancellationToken cancellationToken);

    /// <summary>True when the owner decided about the entry already (accepted or dismissed).</summary>
    ValueTask<bool> IsDecidedAsync(Guid ledgerEntryId, CancellationToken cancellationToken);

    /// <summary>Collects the decision; <see cref="IPortfolioUnitOfWork"/> saves it. A second decision about the same entry makes the save fail.</summary>
    void Add(DividendSuggestion decision);
}

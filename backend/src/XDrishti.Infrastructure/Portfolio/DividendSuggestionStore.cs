using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The suggested dividends (ADR 0026): ledger entries of class dividend without a decision in <c>portfolio.dividend_suggestions</c>. Reads are not tracked; a new decision is collected and saved by the portfolio unit of work (a second decision about the
/// same entry breaks the primary key). Nothing is changed or deleted.
/// </summary>
internal sealed class DividendSuggestionStore(XDrishtiDbContext db) : IDividendSuggestionStore
{
    public async ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListOpenAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.EntryClass == LedgerClass.Dividend
                && x.Amount > 0m && x.Amount <= DividendRules.MaxAmount // an amount that could not be entered as a dividend is not offered
                && !db.DividendSuggestions.Any(d => d.Id == x.Id));
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async ValueTask<LedgerEntry?> FindEntryAsync(Guid ledgerEntryId, CancellationToken cancellationToken) =>
        await db.LedgerEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ledgerEntryId, cancellationToken);

    public async ValueTask<bool> IsDecidedAsync(Guid ledgerEntryId, CancellationToken cancellationToken) =>
        await db.DividendSuggestions.AsNoTracking().AnyAsync(x => x.Id == ledgerEntryId, cancellationToken);

    public void Add(DividendSuggestion decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        db.DividendSuggestions.Add(decision);
    }
}

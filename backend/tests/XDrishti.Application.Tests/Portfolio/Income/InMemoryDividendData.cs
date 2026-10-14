using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>One in-memory stand-in for the dividend ports (dividends and the decisions about suggested ones); the ledger entries are the broker history's. Entities are shared by reference like a tracked DbContext.</summary>
internal sealed class InMemoryDividendData(InMemoryBrokerData broker) : IDividendStore, IDividendSuggestionStore
{
    private readonly List<object> _collected = [];

    public List<Dividend> Dividends { get; } = [];

    public List<DividendSuggestion> Decisions { get; } = [];

    public ValueTask<Dividend?> FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Dividends.FirstOrDefault(d => d.Id == id));

    public void Add(Dividend dividend)
    {
        Dividends.Add(dividend);
        _collected.Add(dividend);
    }

    public ValueTask<bool> ExistsSameAsync(Guid accountId, Guid? holdingId, DateOnly payDate, decimal amount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Dividends.Any(d => d.AccountId == accountId && d.HoldingId == holdingId && d.PayDate == payDate && d.Amount == amount && !d.IsVoided));

    public ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForHoldingAsync(Guid holdingId, int page, int pageSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Page(Dividends.Where(d => d.HoldingId == holdingId), page, pageSize));

    public ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForAccountAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Page(Dividends.Where(d => d.AccountId == accountId), page, pageSize));

    public ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListOpenAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var open = broker.Entries
            .Where(e => e.AccountId == accountId && e.EntryClass == LedgerClass.Dividend && DividendRules.CanEnter(e.Amount) && Decisions.All(d => d.Id != e.Id))
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .ToList();
        return ValueTask.FromResult<(IReadOnlyList<LedgerEntry>, int)>(([.. open.Skip((page - 1) * pageSize).Take(pageSize)], open.Count));
    }

    public ValueTask<LedgerEntry?> FindEntryAsync(Guid ledgerEntryId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(broker.Entries.FirstOrDefault(e => e.Id == ledgerEntryId));

    public ValueTask<bool> IsDecidedAsync(Guid ledgerEntryId, CancellationToken cancellationToken) => ValueTask.FromResult(Decisions.Any(d => d.Id == ledgerEntryId));

    public void Add(DividendSuggestion decision)
    {
        Decisions.Add(decision);
        _collected.Add(decision);
    }

    /// <summary>A save went through: what was collected is committed.</summary>
    public void ForgetCollected() => _collected.Clear();

    /// <summary>A save was refused: the database rolls back what it had collected.</summary>
    public void RollBackCollected()
    {
        Dividends.RemoveAll(_collected.Contains);
        Decisions.RemoveAll(_collected.Contains);
        _collected.Clear();
    }

    private static (IReadOnlyList<Dividend>, int) Page(IEnumerable<Dividend> source, int page, int pageSize)
    {
        var all = source.OrderByDescending(d => d.PayDate).ThenByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).ToList();
        return ([.. all.Skip((page - 1) * pageSize).Take(pageSize)], all.Count);
    }
}

using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Tests.Portfolio.Income;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The dividend import batches in memory; entities are shared by reference like a tracked DbContext.</summary>
internal sealed class InMemoryDividendBatches(InMemoryDividendData dividends) : IDividendBatches
{
    private readonly List<DividendBatch> _collected = [];

    public List<DividendBatch> Batches { get; } = [];

    ValueTask<DividendBatch?> IDividendBatches.FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.FirstOrDefault(b => b.Id == id));

    public ValueTask<DividendBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.Where(b => b.Id == id).Select(Row).FirstOrDefault());

    public ValueTask<(IReadOnlyList<DividendBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Batches.Where(b => accountId is null || b.AccountId == accountId).OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).ToList();
        return ValueTask.FromResult<(IReadOnlyList<DividendBatchRow>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(Row)], all.Count));
    }

    public void Add(DividendBatch batch)
    {
        Batches.Add(batch);
        _collected.Add(batch);
    }

    public void ForgetCollected() => _collected.Clear();

    public void RollBackCollected()
    {
        Batches.RemoveAll(_collected.Contains);
        _collected.Clear();
    }

    private DividendBatchRow Row(DividendBatch batch) => new(batch, dividends.Dividends.Count(d => d.BatchId == batch.Id && !d.IsVoided));
}

/// <summary>The remembered scrip names in memory.</summary>
internal sealed class InMemoryDividendAliases : IDividendAliases
{
    private readonly List<DividendAlias> _collected = [];

    public List<DividendAlias> Aliases { get; } = [];

    /// <summary>How many times the aliases were looked up by key (an import does it once, however many names it remembers).</summary>
    public int FindByKeysReads { get; private set; }

    public ValueTask<IReadOnlyList<DividendAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DividendAlias>>([.. Aliases.Where(a => a.AccountId == accountId)]);

    ValueTask<DividendAlias?> IDividendAliases.FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Aliases.FirstOrDefault(a => a.Id == id));

    public ValueTask<IReadOnlyList<DividendAlias>> FindByKeysAsync(Guid accountId, IReadOnlyCollection<string> nameKeys, CancellationToken cancellationToken)
    {
        FindByKeysReads++;
        return ValueTask.FromResult<IReadOnlyList<DividendAlias>>([.. Aliases.Where(a => a.AccountId == accountId && nameKeys.Contains(a.NameKey))]);
    }

    public void Add(DividendAlias alias)
    {
        Aliases.Add(alias);
        _collected.Add(alias);
    }

    public void ForgetCollected() => _collected.Clear();

    public void RollBackCollected()
    {
        Aliases.RemoveAll(_collected.Contains);
        _collected.Clear();
    }
}

/// <summary>The NSE shares and ETFs of the instrument master (names for matching).</summary>
internal sealed class FakeInstrumentNames : IInstrumentNames
{
    public List<ShareName> Shares { get; } = [];

    public int Reads { get; private set; }

    public ValueTask<IReadOnlyList<ShareName>> ListSharesAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult<IReadOnlyList<ShareName>>([.. Shares]);
    }
}

/// <summary>The holdings of the account for the dividend import (read models), so a test can have as many as it needs with the names it needs.</summary>
internal sealed class FakeDividendHoldings : IHoldingQueries
{
    public List<HoldingData> Holdings { get; } = [];

    public int Reads { get; private set; }

    public ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult<IReadOnlyList<HoldingData>>([.. Holdings.Where(h => filter.AccountId is null || h.AccountId == filter.AccountId)]);
    }

    public ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken) => ValueTask.FromResult(Holdings.FirstOrDefault(h => h.Id == holdingId));
}

using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>One in-memory stand-in for the trade ports (trades, batches, the master list and the applied corporate actions); entities are shared by reference like a tracked DbContext.</summary>
internal sealed class InMemoryTradeData : ITradeStore, ITradeBatches, ITradeReferenceData
{
    public List<Transaction> Trades { get; } = [];

    public List<TradeBatch> Batches { get; } = [];

    /// <summary>The NSE shares and ETFs of the master list.</summary>
    public List<TradeInstrument> Instruments { get; } = [];

    /// <summary>The applied actions by instrument.</summary>
    public Dictionary<Guid, List<AppliedAction>> Actions { get; } = [];

    /// <summary>How many times the corporate actions were read.</summary>
    public int ActionReads { get; private set; }

    /// <summary>How many times the whole history of an account was read.</summary>
    public int WholeHistoryReads { get; private set; }

    public ValueTask<IReadOnlyList<Transaction>> ListActiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        WholeHistoryReads++;
        return ValueTask.FromResult<IReadOnlyList<Transaction>>([.. Trades.Where(t => t.AccountId == accountId && !t.IsVoided).OrderBy(t => t.CreatedAt).ThenBy(t => t.Seq).ThenBy(t => t.Id)]);
    }

    public ValueTask<IReadOnlyList<Transaction>> ListActiveForSecurityAsync(Guid accountId, string? isin, string symbol, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Transaction>>(
        [
            .. Trades
                .Where(t => t.AccountId == accountId && !t.IsVoided && ((isin is not null && t.Isin == isin) || (symbol.Length > 0 && t.TradingSymbol == symbol && (isin is null || t.Isin is null))))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Seq).ThenBy(t => t.Id),
        ]);

    public ValueTask<IReadOnlyList<Transaction>> ListActiveOfBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Transaction>>([.. Trades.Where(t => t.BatchId == batchId && !t.IsVoided).OrderBy(t => t.Seq)]);

    public ValueTask<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Trades.FirstOrDefault(t => t.Id == id));

    public ValueTask<(IReadOnlyList<Transaction> Items, int Total)> ListForSecurityAsync(Guid accountId, string? isin, string symbol, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Trades
            .Where(t => t.AccountId == accountId && ((isin is not null && t.Isin == isin) || (symbol.Length > 0 && t.TradingSymbol == symbol && (isin is null || t.Isin is null))))
            .OrderByDescending(t => t.TradeDate).ThenByDescending(t => t.TradeTime).ThenByDescending(t => t.CreatedAt).ThenByDescending(t => t.Seq).ThenByDescending(t => t.Id)
            .ToList();
        return ValueTask.FromResult<(IReadOnlyList<Transaction>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize)], all.Count));
    }

    public void Add(IEnumerable<Transaction> trades)
    {
        foreach (var trade in trades)
        {
            if (Trades.Any(t => t.AccountId == trade.AccountId && t.ExternalId == trade.ExternalId && !t.IsVoided))
            {
                throw new InvalidOperationException("The partial unique index would refuse this trade.");
            }

            Trades.Add(trade);
        }
    }

    public ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var voided = 0;
        foreach (var trade in Trades.Where(t => t.BatchId == batchId))
        {
            voided += trade.Void(by, at) ? 1 : 0;
        }

        return ValueTask.FromResult(voided);
    }

    ValueTask<TradeBatch?> ITradeBatches.FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.FirstOrDefault(b => b.Id == id));

    public ValueTask<TradeBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.Where(b => b.Id == id).Select(Row).FirstOrDefault());

    public ValueTask<(IReadOnlyList<TradeBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Batches.Where(b => accountId is null || b.AccountId == accountId).OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).ToList();
        return ValueTask.FromResult<(IReadOnlyList<TradeBatchRow>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(Row)], all.Count));
    }

    public void Add(TradeBatch batch) => Batches.Add(batch);

    public ValueTask<IReadOnlyList<TradeInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TradeInstrument>>([.. Instruments.Where(i => (i.Isin is not null && isins.Contains(i.Isin)) || symbols.Contains(i.Symbol))]);

    public ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>> ListAppliedActionsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ActionReads++;
        return ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>>(
            Actions.Where(a => instrumentIds.Contains(a.Key)).ToDictionary(a => a.Key, a => (IReadOnlyList<AppliedAction>)[.. a.Value.OrderBy(x => x.ExDate)]));
    }

    private TradeBatchRow Row(TradeBatch batch)
    {
        var active = Trades.Where(t => t.BatchId == batch.Id && !t.IsVoided).ToList();
        return new TradeBatchRow(batch, active.Count, active.Select(t => t.Isin ?? t.TradingSymbol).Distinct().Count());
    }
}

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

    /// <summary>The Dhan security ids (NSE equity segment) of the instruments, by security id.</summary>
    public Dictionary<string, TradeInstrument> DhanIds { get; } = [];

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

    /// <summary>How many times the securities of the broker trades were read (once per automatic rebuild that was not skipped).</summary>
    public int BrokerSecurityReads { get; private set; }

    public ValueTask<IReadOnlyList<TradeSecurity>> ListBrokerSecuritiesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        BrokerSecurityReads++;
        return ValueTask.FromResult<IReadOnlyList<TradeSecurity>>(
            [.. Trades.Where(t => t.AccountId == accountId && !t.IsVoided && t.Source == TradeSource.Broker).Select(t => new TradeSecurity(t.Isin, t.TradingSymbol)).Distinct().OrderBy(t => t.Isin, StringComparer.Ordinal).ThenBy(t => t.TradingSymbol, StringComparer.Ordinal)]);
    }

    public ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<string>>(Trades.Where(t => t.AccountId == accountId && !t.IsVoided && externalIds.Contains(t.ExternalId)).Select(t => t.ExternalId).ToHashSet(StringComparer.Ordinal));

    public ValueTask<IReadOnlyList<Transaction>> ListBrokerAwaitingEnrichmentAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Transaction>>(
        [
            .. Trades.Where(t => t.AccountId == accountId && !t.IsVoided && t.Source == TradeSource.Broker && externalIds.Contains(t.ExternalId) && (t.Isin is null || t.TradingSymbol.Length == 0 || t.Charges == 0m)),
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

    public ValueTask<IReadOnlyList<TradeGroup>> ListBrokerGroupsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TradeGroup>>(
        [
            .. Trades
                .Where(t => t.AccountId == accountId && !t.IsVoided && t.Source == TradeSource.Broker && t.Isin is not null && t.TradeDate >= from && t.TradeDate <= to)
                .GroupBy(t => (t.TradeDate, Isin: t.Isin!, t.Side))
                .Select(g => new TradeGroup(g.Key.TradeDate, g.Key.Isin, g.Key.Side, g.Sum(t => t.Qty)))
                .OrderBy(g => g.Date).ThenBy(g => g.Isin, StringComparer.Ordinal).ThenBy(g => g.Side),
        ]);

    public ValueTask<SupersedeOutcome> SupersedeBillRowsAsync(Guid accountId, IReadOnlyCollection<TradeGroup> brokerGroups, Guid runId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var wanted = brokerGroups.GroupBy(g => (g.Date, g.Isin, g.Side)).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var hits = Trades.Where(t => t.AccountId == accountId && !t.IsVoided && t.Source == TradeSource.Bill && t.Isin is not null && wanted.ContainsKey((t.TradeDate, t.Isin, t.Side))).ToList();
        var differing = hits.GroupBy(t => (t.TradeDate, t.Isin!, t.Side)).Count(g => g.Sum(t => t.Qty) != wanted[g.Key]);
        var voided = hits.Count(t => t.Supersede(runId, at));
        return ValueTask.FromResult(new SupersedeOutcome(voided, differing));
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
            _collected.Add(trade);
        }
    }

    private readonly List<Transaction> _collected = [];

    /// <summary>A save went through: what was collected is committed.</summary>
    public void ForgetCollected() => _collected.Clear();

    /// <summary>A save was refused: the database rolls back what it had collected.</summary>
    public void RollBackCollected()
    {
        Trades.RemoveAll(_collected.Contains);
        _collected.Clear();
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

    public ValueTask<IReadOnlyDictionary<string, TradeInstrument>> FindInstrumentsByDhanIdsAsync(IReadOnlyCollection<string> securityIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, TradeInstrument>>(DhanIds.Where(d => securityIds.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal));

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

using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>The names that files taught, in memory; entities are shared by reference like a tracked context, a refused save rolls back what was collected.</summary>
internal sealed class InMemorySecurityAliases : ISecurityAliases
{
    private readonly List<SecurityAlias> _collected = [];

    public List<SecurityAlias> Aliases { get; } = [];

    public ValueTask<IReadOnlyList<SecurityAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SecurityAlias>>([.. Aliases.Where(a => a.AccountId == accountId)]);

    public void Add(SecurityAlias alias)
    {
        if (Aliases.Any(a => a.AccountId == alias.AccountId && a.NameKey == alias.NameKey && a.Isin == alias.Isin))
        {
            throw new InvalidOperationException("The unique index would refuse this alias.");
        }

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

/// <summary>The remembered hash of the client code per account, in memory; a refused save rolls back what was collected, a second code of an account is what the primary key would refuse.</summary>
internal sealed class InMemoryReportClientCodes : IReportClientCodes
{
    private readonly List<ReportClientCode> _collected = [];

    public List<ReportClientCode> Codes { get; } = [];

    public ValueTask<string?> FindHashAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Codes.FirstOrDefault(c => c.AccountId == accountId)?.Hash);

    public void Add(ReportClientCode code)
    {
        if (Codes.Any(c => c.AccountId == code.AccountId))
        {
            throw new InvalidOperationException("The primary key would refuse a second client code of the account.");
        }

        Codes.Add(code);
        _collected.Add(code);
    }

    public void ForgetCollected() => _collected.Clear();

    public void RollBackCollected()
    {
        Codes.RemoveAll(_collected.Contains);
        _collected.Clear();
    }
}

/// <summary>The cost import batches in memory; the entries that still count are the ledger entries of the batch that are not voided.</summary>
internal sealed class InMemoryCostBatches(InMemoryBrokerData ledger) : ICostBatches
{
    private readonly List<CostBatch> _collected = [];

    public List<CostBatch> Batches { get; } = [];

    ValueTask<CostBatch?> ICostBatches.FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.FirstOrDefault(b => b.Id == id));

    public ValueTask<CostBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Batches.Where(b => b.Id == id).Select(Row).FirstOrDefault());

    public ValueTask<(IReadOnlyList<CostBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Batches.Where(b => accountId is null || b.AccountId == accountId).OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).ToList();
        return ValueTask.FromResult<(IReadOnlyList<CostBatchRow>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(Row)], all.Count));
    }

    public void Add(CostBatch batch)
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

    private CostBatchRow Row(CostBatch batch) => new(batch, ledger.Entries.Count(e => e.BatchId == batch.Id && !e.IsVoided));
}

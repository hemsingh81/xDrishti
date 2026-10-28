using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>An in-memory <see cref="ILiveExecutionStore"/> with the real unique keys: one execution per outcome, one class per order, one snapshot per week and key.</summary>
internal sealed class InMemoryExecutionStore : ILiveExecutionStore
{
    public List<StoredLiveExecution> Executions { get; } = [];
    public List<LiveOrderClassRow> Classes { get; } = [];
    public List<NewSlippageEstimate> Slippage { get; } = [];
    public List<NewCapacityScore> Capacity { get; } = [];

    public ValueTask<int> InsertExecutionsAsync(IReadOnlyList<NewLiveExecution> rows, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var r in rows.Where(r => Executions.All(x => x.Row.OutcomeId != r.OutcomeId)))
        {
            Executions.Add(new StoredLiveExecution(Executions.Count + 1, r, DateTimeOffset.UnixEpoch));
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<IReadOnlySet<long>> ExecutionOutcomeIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<long>>(Executions.Where(e => e.Row.AccountId == accountId && e.Row.ForDate == date).Select(e => e.Row.OutcomeId).ToHashSet());

    public ValueTask<int> InsertOrderClassesAsync(IReadOnlyList<LiveOrderClassRow> rows, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var r in rows.Where(r => !Classes.Any(x => x.AccountId == r.AccountId && x.BrokerOrderId == r.BrokerOrderId)))
        {
            Classes.Add(r);
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<IReadOnlySet<string>> ClassifiedOrderIdsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<string>>(Classes.Where(c => c.AccountId == accountId && c.ForDate == date).Select(c => c.BrokerOrderId).ToHashSet());

    public ValueTask<StoredExecutionPage> ListExecutionsAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken)
    {
        var all = Executions.Where(e => (accountId is null || e.Row.AccountId == accountId) && e.Row.ForDate >= from && e.Row.ForDate <= to).OrderByDescending(e => e.Row.ForDate).ThenByDescending(e => e.Id).ToList();
        return ValueTask.FromResult(new StoredExecutionPage([.. all.Skip(skip).Take(take)], all.Count));
    }

    public ValueTask<StoredLiveExecution?> FindExecutionAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Executions.FirstOrDefault(e => e.Id == id));

    public ValueTask<IReadOnlyList<StoredLiveExecution>> ExecutionsUntilAsync(DateOnly until, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredLiveExecution>>([.. Executions.Where(e => e.Row.ForDate <= until)]);

    public ValueTask<IReadOnlyList<LiveOrderClassRow>> OrderClassesUntilAsync(DateOnly until, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LiveOrderClassRow>>([.. Classes.Where(c => c.ForDate <= until)]);

    public ValueTask<(int Count, DateOnly? First, DateOnly? Last)> ExecutionSummaryAsync(DateOnly until, CancellationToken cancellationToken)
    {
        var rows = Executions.Where(e => e.Row.ForDate <= until).ToList();
        return ValueTask.FromResult((rows.Count, rows.Count > 0 ? (DateOnly?)rows.Min(e => e.Row.ForDate) : null, rows.Count > 0 ? (DateOnly?)rows.Max(e => e.Row.ForDate) : null));
    }

    public ValueTask<int> InsertSlippageEstimatesAsync(IReadOnlyList<NewSlippageEstimate> rows, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var r in rows.Where(r => !Slippage.Any(x => x.WeekStart == r.WeekStart && x.Bucket == r.Bucket && x.Side == r.Side && x.Style == r.Style && x.ReferenceKind == r.ReferenceKind)))
        {
            Slippage.Add(r);
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<int> InsertCapacityScoresAsync(IReadOnlyList<NewCapacityScore> rows, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var r in rows.Where(r => !Capacity.Any(x => x.WeekStart == r.WeekStart && x.Scope == r.Scope && x.ScopeKey == r.ScopeKey)))
        {
            Capacity.Add(r);
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<(DateOnly? Week, IReadOnlyList<NewSlippageEstimate> Rows)> SlippageEstimatesAsync(DateOnly? weekStart, CancellationToken cancellationToken)
    {
        var week = weekStart ?? (Slippage.Count > 0 ? Slippage.Max(s => s.WeekStart) : (DateOnly?)null);
        var rows = Slippage.Where(s => s.WeekStart == week).ToList();
        return ValueTask.FromResult<(DateOnly?, IReadOnlyList<NewSlippageEstimate>)>((rows.Count > 0 ? week : null, rows));
    }

    public ValueTask<(DateOnly? Week, IReadOnlyList<NewCapacityScore> Rows)> CapacityScoresAsync(DateOnly? weekStart, CancellationToken cancellationToken)
    {
        var week = weekStart ?? (Capacity.Count > 0 ? Capacity.Max(s => s.WeekStart) : (DateOnly?)null);
        var rows = Capacity.Where(s => s.WeekStart == week).ToList();
        return ValueTask.FromResult<(DateOnly?, IReadOnlyList<NewCapacityScore>)>((rows.Count > 0 ? week : null, rows));
    }
}

/// <summary>The stored market facts of the fixture day: tick 0.05, turnover 640 crore (L1), the 09:40 bar opens at 1,250.20, and the lows of an order's life reach 1,250.90.</summary>
internal sealed class FakeLiveMarket : ILiveExecutionMarket
{
    public decimal? Tick { get; set; } = 0.05m;
    public decimal? Adv { get; set; } = 640m;
    public decimal? Open { get; set; } = 1250.20m;
    public (decimal? Low, decimal? High) Extremes { get; set; } = (1250.90m, 1255m);

    public ValueTask<decimal?> TickAsync(string symbol, CancellationToken cancellationToken) => ValueTask.FromResult(Tick);

    public ValueTask<decimal?> AdvTurnoverCrAsync(string symbol, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(symbol == "TCS" ? (decimal?)null : Adv);

    public ValueTask<decimal?> MinuteOpenAsync(string symbol, DateTimeOffset at, CancellationToken cancellationToken) => ValueTask.FromResult(Open);

    public ValueTask<(decimal? Low, decimal? High)> MinuteExtremesAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => ValueTask.FromResult(Extremes);
}

/// <summary>The estimate settings of the spec's defaults.</summary>
internal sealed class FakeEstimateSettings : ILiveEstimateSettings
{
    public bool Enabled => false;

    public BucketEdges Edges => BucketEdges.Default;

    public int MinN { get; set; } = 10;

    public int ProposalMinN { get; set; } = 20;

    public int CapacityPriorStrength => 10;

    public int CapacityMinN { get; set; } = 10;
}

using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>Holdings as the history reads them: a fixed list, filtered by account, bucket and status like the database query does.</summary>
internal sealed class FakeHoldingQueries(params HoldingData[] holdings) : IHoldingQueries
{
    public List<HoldingData> Holdings { get; } = [.. holdings];

    public int ListCalls { get; private set; }

    public ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken)
    {
        ListCalls++;
        var rows = Holdings.AsEnumerable();
        if (filter.AccountId is { } account)
        {
            rows = rows.Where(h => h.AccountId == account);
        }

        if (filter.BucketId is { } bucket)
        {
            rows = rows.Where(h => h.BucketId == bucket);
        }

        if (filter.Status is { } status)
        {
            rows = rows.Where(h => h.Status == status);
        }

        return ValueTask.FromResult<IReadOnlyList<HoldingData>>([.. rows]);
    }

    public ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken) => ValueTask.FromResult(Holdings.FirstOrDefault(h => h.Id == holdingId));
}

/// <summary>The stored day closes and applied actions by instrument; counts the reads and remembers what was asked.</summary>
internal sealed class FakePortfolioPrices : IPortfolioPrices
{
    public Dictionary<Guid, StoredPrices> Stored { get; } = [];

    public int Calls { get; private set; }

    public (IReadOnlyCollection<Guid> Ids, DateOnly From, DateOnly To)? Last { get; private set; }

    public void Set(Guid instrumentId, IEnumerable<(DateOnly Date, double Close)> closes, params XDrishti.Domain.Market.Actions.AppliedAction[] actions) =>
        Stored[instrumentId] = new StoredPrices([.. closes.Select(c => new StoredClose(c.Date, c.Close))], actions);

    public ValueTask<IReadOnlyDictionary<Guid, StoredPrices>> ReadDailyAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken)
    {
        Calls++;
        Last = ([.. instrumentIds], from, toInclusive);
        var answer = new Dictionary<Guid, StoredPrices>();
        foreach (var id in instrumentIds.Where(Stored.ContainsKey))
        {
            var closes = Stored[id].Closes.Where(c => c.Date >= from && c.Date <= toInclusive).ToList();
            if (closes.Count > 0)
            {
                answer[id] = new StoredPrices(closes, Stored[id].Actions);
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<Guid, StoredPrices>>(answer);
    }
}

/// <summary>The master list's indexes and the stored benchmark choice in memory; a change is only visible after the save (the unit of work).</summary>
internal sealed class FakePortfolioBenchmark : IPortfolioBenchmark, IPortfolioUnitOfWork, IAccountEventLog
{
    private Guid? _pending;

    public Dictionary<Guid, BenchmarkInstrument> Instruments { get; } = [];

    public Guid? Chosen { get; set; }

    public Guid? Default { get; set; }

    public List<AccountEvent> Events { get; } = [];

    public int Saves { get; private set; }

    public bool RefuseNextSave { get; set; }

    public BenchmarkInstrument Add(string symbol, string name, string type = "index", bool retired = false, bool tracked = false, bool hasBars = false)
    {
        var instrument = new BenchmarkInstrument(Guid.CreateVersion7(), symbol, name, type, retired, tracked, hasBars);
        Instruments[instrument.Id] = instrument;
        return instrument;
    }

    public ValueTask<ConfiguredBenchmark?> GetAsync(CancellationToken cancellationToken)
    {
        if (Chosen is { } chosen && Instruments.TryGetValue(chosen, out var own))
        {
            return ValueTask.FromResult<ConfiguredBenchmark?>(new ConfiguredBenchmark(own, false));
        }

        return ValueTask.FromResult(Default is { } id && Instruments.TryGetValue(id, out var fallback) ? new ConfiguredBenchmark(fallback, true) : null);
    }

    public ValueTask<BenchmarkInstrument?> FindAsync(Guid instrumentId, CancellationToken cancellationToken) => ValueTask.FromResult(Instruments.GetValueOrDefault(instrumentId));

    public ValueTask SetAsync(Guid instrumentId, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _pending = instrumentId;
        return ValueTask.CompletedTask;
    }

    public ValueTask SaveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        if (RefuseNextSave)
        {
            RefuseNextSave = false;
            _pending = null;
            return ValueTask.FromResult(false);
        }

        Chosen = _pending ?? Chosen;
        _pending = null;
        return ValueTask.FromResult(true);
    }

    public ValueTask<Domain.Common.Result> RunAsync(Func<CancellationToken, ValueTask<Domain.Common.Result>> work, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Domain.Common.Result> RunOnceAsync(Func<CancellationToken, ValueTask<Domain.Common.Result>> work, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Record(AccountEvent accountEvent) => Events.Add(accountEvent);

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);
}

/// <summary>The cache as the infrastructure keeps it, without a clock: entries stay until invalidated.</summary>
internal sealed class FakeHistoryCache : IPortfolioHistoryCache
{
    private readonly Dictionary<string, object> _entries = [];

    public long Version { get; private set; }

    public int Sets { get; private set; }

    public int Hits { get; private set; }

    public bool TryGet<T>(string key, out T? value)
        where T : class
    {
        if (_entries.TryGetValue(key, out var found) && found is T typed)
        {
            Hits++;
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public void Set<T>(string key, T value, long version)
        where T : class
    {
        if (version == Version)
        {
            Sets++;
            _entries[key] = value;
        }
    }

    public void Invalidate()
    {
        Version++;
        _entries.Clear();
    }
}

/// <summary>A trading calendar that is the same for every read.</summary>
internal sealed class FakeTradingCalendar(TradingCalendar calendar) : ITradingCalendar
{
    public int Reads { get; private set; }

    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult(calendar);
    }

    public void Invalidate()
    {
    }
}

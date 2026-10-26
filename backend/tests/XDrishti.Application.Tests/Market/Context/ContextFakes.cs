using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Context;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

internal sealed class FakeContextSettings : IContextSettings
{
    public bool Enabled { get; set; } = true;

    public IReadOnlyList<string> IndexSymbols { get; set; } = ["NSE:NIFTY"];

    public IReadOnlyList<string> VixSymbols { get; set; } = ["NSE:INDIA_VIX"];

    public int VixGraceSessions { get; set; } = 5;

    public double MinDayFraction { get; set; } = 0.96;

    public int BreadthSmaSessions { get; set; } = 20;

    public int BreadthMinVoters { get; set; } = 50;

    public double BreadthMinShare { get; set; } = 0.5;

    public int BreadthGraceSessions { get; set; } = 5;
}

internal sealed class FakeContextBarSource : IContextBarSource
{
    public ContextLegs Legs { get; set; } = new(null, null, [], []);

    public int Reads { get; private set; }

    public ValueTask<ContextLegs> ReadAsync(IReadOnlyList<string> indexSymbols, IReadOnlyList<string> vixSymbols, CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult(Legs);
    }
}

/// <summary>The stored days in memory, with the insert-only rule of the table: a stored date is never replaced.</summary>
internal sealed class FakeContextDayStore : IContextDayStore
{
    public List<ContextDayRecord> Rows { get; } = [];

    public Guid? InstrumentOfLastInsert { get; private set; }

    public ValueTask<ContextStamp> StampAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContextStamp(Rows.Count, Rows.Count == 0 ? null : Rows.Max(r => r.Date)));

    public ValueTask<IReadOnlyList<DateOnly>> ListDatesAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. Rows.Select(r => r.Date).Order()]);

    public ValueTask<IReadOnlyList<ContextDayRecord>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ContextDayRecord>>([.. Rows.OrderBy(r => r.Date)]);

    public ValueTask<int> InsertAsync(Guid indexInstrumentId, IReadOnlyList<ContextDayRecord> records, CancellationToken cancellationToken)
    {
        InstrumentOfLastInsert = indexInstrumentId;
        var inserted = 0;
        foreach (var record in records.Where(r => Rows.All(x => x.Date != r.Date)))
        {
            Rows.Add(record);
            inserted++;
        }

        return ValueTask.FromResult(inserted);
    }

    /// <summary>The breadth of each day that has one, with the same once-only rule as the table.</summary>
    public Dictionary<DateOnly, StoredBreadth> Breadth { get; } = [];

    public int BreadthWrites { get; private set; }

    public ValueTask<IReadOnlyList<StoredBreadth>> ListBreadthAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredBreadth>>([.. Rows.OrderBy(r => r.Date).Select(r => Breadth.GetValueOrDefault(r.Date) ?? new StoredBreadth(r.Date, null, null, null, null, null))]);

    public ValueTask<int> SetBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var write in writes)
        {
            var state = Breadth.GetValueOrDefault(write.Date)?.State;
            if (Rows.Any(r => r.Date == write.Date) && (state is null || (write.ReplaceInsufficient && state == BreadthResult.Insufficient)))
            {
                Put(write);
                changed++;
            }
        }

        return ValueTask.FromResult(changed);
    }

    public ValueTask<int> ReplaceBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken)
    {
        foreach (var write in writes.Where(w => Rows.Any(r => r.Date == w.Date)))
        {
            Put(write);
        }

        return ValueTask.FromResult(writes.Count);
    }

    private void Put(BreadthWrite write)
    {
        BreadthWrites++;
        Breadth[write.Date] = new StoredBreadth(write.Date, write.Result.Value, write.Result.Voters, write.Result.Universe, write.Result.Excluded, write.Result.State);
    }
}

internal sealed class FakeBreadthSource : IBreadthSource
{
    public BreadthData Data { get; set; } = new([], new Dictionary<Guid, IReadOnlySet<DateOnly>>());

    public List<DateOnly> Reads { get; } = [];

    public ValueTask<BreadthData> ReadAsync(DateOnly from, CancellationToken cancellationToken)
    {
        Reads.Add(from);
        return ValueTask.FromResult(Data);
    }
}

/// <summary>Stands in for the context upkeep of a data job and records that it ran.</summary>
internal sealed class FakeContextUpkeep : IContextUpkeep
{
    public int Runs { get; private set; }

    public ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        Runs++;
        return ValueTask.CompletedTask;
    }
}

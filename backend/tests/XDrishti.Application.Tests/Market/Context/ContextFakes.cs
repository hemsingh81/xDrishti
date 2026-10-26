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

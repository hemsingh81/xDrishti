using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Baskets.Derived;

internal sealed class FakeDerivedSettings : IDerivedSettings
{
    public bool Enabled { get; set; } = true;

    public int MaxBackfillDays { get; set; } = 400;

    public int ScoresKeptBeyondMembers { get; set; } = 20;

    public string Snapshot() => "{}";
}

internal sealed class FakeDerivedLock : IDerivedRunLock
{
    public bool Held { get; set; }

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable?>(Held ? null : new Handle());

    private sealed class Handle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeCalendar(params DateOnly[] holidays) : ITradingCalendar
{
    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TradingCalendar(holidays.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))));

    public void Invalidate()
    {
    }
}

/// <summary>Counts every read; the universe is whatever the test put in.</summary>
internal sealed class FakeUniverse : IDerivedUniverseReader
{
    public List<UniverseInstrument> Instruments { get; } = [];

    public int Reads { get; private set; }

    public ValueTask<IReadOnlyList<UniverseInstrument>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult<IReadOnlyList<UniverseInstrument>>(Instruments);
    }
}

/// <summary>An in-memory version store with the same rules as the real one for what the handlers rely on (idempotency, behind a newer published version).</summary>
internal sealed class FakeVersionStore : IDerivedVersionStore
{
    public List<DerivedDefinitionRow> Definitions { get; } = [];

    public List<DerivedVersionWrite> Versions { get; } = [];

    public List<NewParamSet> ParamSets { get; } = [];

    public int ListCalls { get; private set; }

    public bool ThrowOnWrite { get; set; }

    public ValueTask<IReadOnlyList<DerivedDefinitionRow>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        ListCalls++;
        return ValueTask.FromResult<IReadOnlyList<DerivedDefinitionRow>>([.. Definitions]);
    }

    public ValueTask<PreviousVersion?> PreviousAsync(Guid basketId, DateOnly asOf, CancellationToken cancellationToken, string provenance = BasketProvenance.Derived)
    {
        var version = Versions.Where(v => v.BasketId == basketId && v.Provenance == provenance && v.AsOf < asOf).OrderByDescending(v => v.AsOf).FirstOrDefault();
        return ValueTask.FromResult(version is null ? null : DerivedPrevious(version));
    }

    public ValueTask<(DateOnly? FirstEffectiveFrom, DateOnly? NewestAsOf)> PublishedRangeAsync(Guid basketId, CancellationToken cancellationToken)
    {
        var live = Versions.Where(v => v.BasketId == basketId && v.Provenance == BasketProvenance.Derived).ToList();
        return ValueTask.FromResult<(DateOnly?, DateOnly?)>(live.Count == 0 ? (null, null) : (live.Min(v => v.EffectiveFrom), live.Max(v => v.AsOf)));
    }

    public ValueTask<bool> HasOtherRecomputedAsync(Guid basketId, DateOnly from, DateOnly to, string paramsHash, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Versions.Any(v => v.BasketId == basketId && v.Provenance == BasketProvenance.Recomputed && v.AsOf >= from && v.AsOf <= to && v.ParamsHash != paramsHash));

    public ValueTask<bool> ExistsAsync(Guid basketId, DateOnly asOf, string paramsHash, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Versions.Any(v => v.BasketId == basketId && v.AsOf == asOf && v.ParamsHash == paramsHash));

    public ValueTask<DerivedWriteResult> WriteAsync(DerivedVersionWrite version, CancellationToken cancellationToken)
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("boom");
        }

        if (version.Provenance == BasketProvenance.Derived && Versions.Any(v => v.BasketId == version.BasketId && v.Provenance == BasketProvenance.Derived && v.AsOf > version.AsOf))
        {
            return ValueTask.FromResult(new DerivedWriteResult(DerivedWriteResult.Behind, 0, 0));
        }

        Versions.Add(version);
        return ValueTask.FromResult(new DerivedWriteResult(VersionOutcomes.Applied, version.Scores.Count(s => s.IsMember), 0));
    }

    public ValueTask<BasketVersionPage?> ListVersionsAsync(Guid basketId, int skip, int take, CancellationToken cancellationToken) => ValueTask.FromResult<BasketVersionPage?>(null);

    public ValueTask<BasketVersionDetail?> GetVersionAsync(Guid basketId, Guid versionId, CancellationToken cancellationToken) => ValueTask.FromResult<BasketVersionDetail?>(null);

    public ValueTask<bool> SaveDefinitionAsync(Guid basketId, bool? enabled, NewParamSet? paramSet, string auditText, string actor, CancellationToken cancellationToken)
    {
        var i = Definitions.FindIndex(d => d.BasketId == basketId);
        if (i < 0)
        {
            return ValueTask.FromResult(false);
        }

        var definition = Definitions[i];
        if (paramSet is not null)
        {
            ParamSets.Add(paramSet);
            definition = definition with { ParamSetId = paramSet.Id, Overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(paramSet.ParamsJson)! };
        }

        Definitions[i] = enabled is { } on ? definition with { Enabled = on } : definition;
        return ValueTask.FromResult(true);
    }

    private static PreviousVersion DerivedPrevious(DerivedVersionWrite v) => new(v.VersionId, v.AsOf, v.EffectiveFrom, v.Scores);
}

/// <summary>Everything a derived-basket handler needs, with a clock at Friday 2026-10-23 16:00 India time (the session has closed).</summary>
internal sealed class DerivedWorld
{
    public static readonly DateOnly Friday = new(2026, 10, 23);

    public FakeDerivedSettings Settings { get; } = new();

    public FakeVersionStore Store { get; } = new();

    public FakeUniverse Universe { get; } = new();

    public FakeDerivedLock Lock { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 23, 16, 0, 0, TimeSpan.FromMinutes(330)));

    public ITradingCalendar Calendar { get; set; } = new FakeCalendar();

    /// <summary>The basket "Derived: Most liquid" (definition 3, daily); its top-k is lowered by the factory's topK when given.</summary>
    public DerivedDefinitionRow Liquid { get; private set; } = null!;

    public static DerivedWorld WithLiquidBasket(int instruments, int bars = 70, int? topK = null, bool enabled = true)
    {
        var world = new DerivedWorld();
        world.Liquid = new DerivedDefinitionRow(Guid.NewGuid(), "Derived: Most liquid", 3, enabled, null, topK is { } k ? new Dictionary<string, double> { ["Basket3.TopK"] = k } : new Dictionary<string, double>());
        world.Store.Definitions.Add(world.Liquid);
        for (var i = 0; i < instruments; i++)
        {
            world.AddInstrument($"S{i:D3}", i + 1, bars);
        }

        return world;
    }

    /// <summary>An instrument with <paramref name="bars"/> weekday bars ending on <paramref name="end"/>; the volume (so the traded value) is the rank key of basket 3.</summary>
    public UniverseInstrument AddInstrument(string symbol, double volume, int bars, DateOnly? end = null)
    {
        var instrument = new UniverseInstrument(Guid.NewGuid(), new InstrumentBars(symbol, Weekdays(end ?? Friday, bars, volume)));
        Universe.Instruments.Add(instrument);
        return instrument;
    }

    public static Bar[] Weekdays(DateOnly end, int count, double volume)
    {
        var bars = new List<Bar>();
        for (var date = end; bars.Count < count; date = date.AddDays(-1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                bars.Add(new Bar(date, 100, 101, 99, 100, volume));
            }
        }

        bars.Reverse();
        return [.. bars];
    }

    public RefreshDerivedBasketsHandler Refresh() => new(Settings, Store, Universe, Lock, Calendar, Clock, NullLogger<RefreshDerivedBasketsHandler>.Instance);

    public BackfillDerivedBasketHandler Backfill() => new(Settings, Store, Universe, Lock, Calendar, Clock);

    public SetDerivedParamsHandler SetParams() => new(Store, Settings, Clock);
}

using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market.Context;
using XDrishti.Indicators;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Tests.Market.Context;

/// <summary>P3-01d criteria 4, 5 and 9: the context built from the stored days is the context built from the same records, a read never sees a day on or after its date, and what cannot be read says why.</summary>
public sealed class ContextFiguresAdapterTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    private sealed class MemoryStore : IContextDayStore
    {
        public List<ContextDayRecord> Rows { get; } = [];

        public int Lists { get; private set; }

        public ValueTask<ContextStamp> StampAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new ContextStamp(Rows.Count, Rows.Count == 0 ? null : Rows[^1].Date, Rows.Count(r => r.Breadth is not null), null, Rows.Sum(r => r.Breadth ?? 0)));

        public ValueTask<IReadOnlyList<DateOnly>> ListDatesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. Rows.Select(r => r.Date)]);

        public ValueTask<IReadOnlyList<ContextDayRecord>> ListAsync(CancellationToken cancellationToken)
        {
            Lists++;
            return ValueTask.FromResult<IReadOnlyList<ContextDayRecord>>([.. Rows]);
        }

        public ValueTask<int> InsertAsync(Guid indexInstrumentId, IReadOnlyList<ContextDayRecord> records, CancellationToken cancellationToken)
        {
            Rows.AddRange(records);
            return ValueTask.FromResult(records.Count);
        }

        public ValueTask<IReadOnlyList<StoredBreadth>> ListBreadthAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredBreadth>>([]);

        public ValueTask<int> SetBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) => ValueTask.FromResult(0);

        public ValueTask<int> ReplaceBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<ContextDayRecord> Days(int seed, int count)
    {
        var random = new Random(seed);
        var rows = new List<ContextDayRecord>();
        var close = 20_000.0;
        var date = Start;
        while (rows.Count < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                close *= 1 + (random.NextDouble() - 0.5) * 0.02;
                var high = close * (1 + random.NextDouble() * 0.01);
                var low = close * (1 - random.NextDouble() * 0.01);
                rows.Add(new ContextDayRecord(date, high, low, close, random.Next(10) == 0 ? null : 10 + random.NextDouble() * 20, 375, 375));
            }

            date = date.AddDays(1);
        }

        return rows;
    }

    private static ContextFiguresAdapter Adapter(MemoryStore store) => new(store, new ContextFiguresAdapter.Cache());

    private static IEnumerable<double?> Ten(ContextFigures f) => [f.IndexTrend, f.IndexAdx, f.IndexVolPct, f.Vix, f.VixPct, f.VixChg, f.RegimeTrend, f.RegimeVol, f.Regime, f.Breadth];

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task The_context_from_the_stored_days_has_the_same_ten_values_as_one_built_from_the_same_records_at_every_date(int seed)
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(seed, 320));
        var direct = MarketContext.Create(store.Rows.Select(r => new ContextDay(r.Date, r.IndexHigh, r.IndexLow, r.IndexClose, r.VixClose))).Value;
        var adapter = Adapter(store);

        foreach (var row in store.Rows.Skip(1))
        {
            var read = await adapter.AsOfAsync(row.Date, Ct);
            direct.TryAsOf(row.Date, out var values).ShouldBeTrue();

            read.Figures.ShouldNotBeNull();
            read.Figures.AsOf.ShouldBe(values.Date);
            Ten(read.Figures).ShouldBe(Ten(ContextFiguresAdapter.Figures(values)));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task The_values_of_the_first_days_do_not_change_when_later_rows_are_added(int seed)
    {
        var all = Days(seed, 320);
        var first = new MemoryStore();
        first.Rows.AddRange(all.Take(200));
        var second = new MemoryStore();
        second.Rows.AddRange(all);

        foreach (var row in all.Take(199))
        {
            var before = await Adapter(first).AsOfAsync(row.Date.AddDays(1), Ct);
            var after = await Adapter(second).AsOfAsync(row.Date.AddDays(1), Ct);

            Ten(after.Figures!).ShouldBe(Ten(before.Figures!));
            after.Figures!.Basis.ShouldBe(before.Figures!.Basis);
        }
    }

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 20)];

    [Fact]
    public async Task A_read_never_returns_a_value_dated_on_or_after_the_date_asked()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(3, 100));
        var adapter = Adapter(store);

        foreach (var row in store.Rows)
        {
            var read = await adapter.AsOfAsync(row.Date, Ct);

            if (read.Figures is { } figures)
            {
                figures.AsOf.ShouldBeLessThan(row.Date);
            }
        }
    }

    [Fact]
    public async Task Before_the_first_day_and_without_any_day_the_answer_is_unavailable_with_the_reason()
    {
        var empty = await Adapter(new MemoryStore()).AsOfAsync(Start, Ct);
        var store = new MemoryStore();
        store.Rows.AddRange(Days(1, 10));
        var before = await Adapter(store).AsOfAsync(store.Rows[0].Date, Ct);

        (empty.Figures, empty.Unavailable).ShouldBe((null, "no_context"));
        (before.Figures, before.Unavailable).ShouldBe((null, "before_history"));
    }

    [Fact]
    public async Task Past_the_age_bound_the_answer_is_unavailable_as_stale_and_at_exactly_the_bound_it_is_read()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(1, 10));
        var last = store.Rows[^1].Date;
        var adapter = Adapter(store);

        var atBound = await adapter.AsOfAsync(last.AddDays(7), Ct);
        var past = await adapter.AsOfAsync(last.AddDays(8), Ct);

        atBound.Figures!.AsOf.ShouldBe(last);
        (past.Figures, past.Unavailable).ShouldBe((null, "stale"));
    }

    [Fact]
    public async Task Values_that_are_not_ready_are_null_and_the_basis_is_index_only_without_a_vix_percentile()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(1, 30));
        var read = await Adapter(store).AsOfAsync(store.Rows[^1].Date.AddDays(1), Ct);

        read.Figures!.IndexTrend.ShouldBeNull("55 days are needed");
        read.Figures.VixPct.ShouldBeNull("252 VIX days are needed");
        read.Figures.Regime.ShouldBeNull();
        read.Figures.Basis.ShouldBe("index_only");
        read.Figures.Breadth.ShouldBeNull();
    }

    [Fact]
    public async Task With_enough_history_the_regime_is_ready_and_the_basis_says_whether_the_vix_took_part()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(5, 400).Select(r => r with { VixClose = 15 + r.IndexClose % 7 }));

        var read = await Adapter(store).AsOfAsync(store.Rows[^1].Date.AddDays(1), Ct);

        read.Figures!.Regime.ShouldNotBeNull();
        read.Figures.VixPct.ShouldNotBeNull();
        read.Figures.Basis.ShouldBe("index_and_vix");
    }

    [Fact]
    public async Task The_context_is_built_once_and_rebuilt_when_the_stored_days_change()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(2, 20));
        var adapter = Adapter(store);

        await adapter.AsOfAsync(Start.AddDays(30), Ct);
        await adapter.AsOfAsync(Start.AddDays(31), Ct);
        var builtOnce = store.Lists;
        store.Rows.AddRange(Days(2, 21).Skip(20));
        await adapter.AsOfAsync(Start.AddDays(40), Ct);

        (builtOnce, store.Lists).ShouldBe((1, 2));
    }

    [Fact]
    public async Task A_row_the_library_refuses_gives_context_invalid_not_a_guess()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(1, 5));
        store.Rows[2] = store.Rows[2] with { IndexClose = store.Rows[2].IndexHigh * 2 };

        var read = await Adapter(store).AsOfAsync(Start.AddDays(60), Ct);

        (read.Figures, read.Unavailable).ShouldBe((null, "context_invalid"));
    }

    // P3-01e criterion 9: the breadth feature reads the previous session's stored value, in percent.

    [Fact]
    public async Task The_breadth_of_the_latest_day_before_the_date_is_read_in_percent_with_the_shares_asked_and_a_day_without_one_is_null()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(4, 30).Select((r, i) => i == 10 ? r : r with { Breadth = 20 + i, BreadthUniverse = 214 }));
        var adapter = Adapter(store);

        var withBreadth = await adapter.AsOfAsync(store.Rows[11].Date, Ct);
        var without = await adapter.AsOfAsync(store.Rows[11].Date.AddDays(0), Ct);
        var gap = await adapter.AsOfAsync(store.Rows[10].Date.AddDays(1), Ct);

        withBreadth.Figures!.AsOf.ShouldBe(store.Rows[10].Date, "strictly before the date asked");
        withBreadth.Figures.Breadth.ShouldBeNull("the row of that day has no breadth: NaN of the library, null here");
        withBreadth.Figures.BreadthUniverse.ShouldBeNull();
        without.Figures!.Breadth.ShouldBeNull();
        gap.Figures!.AsOf.ShouldBe(store.Rows[10].Date);
        var next = await adapter.AsOfAsync(store.Rows[12].Date, Ct);
        next.Figures!.Breadth.ShouldBe(31.0);
        next.Figures.BreadthUniverse.ShouldBe(214);
    }

    [Fact]
    public async Task A_breadth_that_is_set_later_makes_the_context_rebuild_without_new_days()
    {
        var store = new MemoryStore();
        store.Rows.AddRange(Days(2, 20));
        var adapter = Adapter(store);
        var before = await adapter.AsOfAsync(Start.AddDays(30), Ct);
        store.Rows[^1] = store.Rows[^1] with { Breadth = 61, BreadthUniverse = 100 };

        var after = await adapter.AsOfAsync(Start.AddDays(30), Ct);

        (before.Figures!.Breadth, after.Figures!.Breadth).ShouldBe((null, 61.0));
    }
}

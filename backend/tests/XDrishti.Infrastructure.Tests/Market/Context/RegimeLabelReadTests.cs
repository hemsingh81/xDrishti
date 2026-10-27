using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market.Context;
using XDrishti.Indicators;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Tests.Market.Context;

/// <summary>P6-02 (ADR 0146): the regime label of a date read through the stored context: only where it is ready, only from days before the date, equal one at a time and in a batch, and with the names of the indicator library.</summary>
public sealed class RegimeLabelReadTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    private sealed class MemoryStore(List<ContextDayRecord> rows) : IContextDayStore
    {
        public int Lists { get; private set; }

        public ValueTask<ContextStamp> StampAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new ContextStamp(rows.Count, rows.Count == 0 ? null : rows[^1].Date));

        public ValueTask<IReadOnlyList<DateOnly>> ListDatesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. rows.Select(r => r.Date)]);

        public ValueTask<IReadOnlyList<ContextDayRecord>> ListAsync(CancellationToken cancellationToken)
        {
            Lists++;
            return ValueTask.FromResult<IReadOnlyList<ContextDayRecord>>([.. rows]);
        }

        public ValueTask<int> InsertAsync(Guid indexInstrumentId, IReadOnlyList<ContextDayRecord> records, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<StoredBreadth>> ListBreadthAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredBreadth>>([]);

        public ValueTask<int> SetBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) => ValueTask.FromResult(0);

        public ValueTask<int> ReplaceBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 8)];

    private static List<ContextDayRecord> Days(int seed, int count, bool vix = true)
    {
        var random = new Random(seed);
        var rows = new List<ContextDayRecord>();
        var close = 20_000.0;
        var date = Start;
        while (rows.Count < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                close *= 1 + ((random.NextDouble() - 0.5) * 0.02);
                var high = close * (1 + (random.NextDouble() * 0.01));
                var low = close * (1 - (random.NextDouble() * 0.01));
                rows.Add(new ContextDayRecord(date, high, low, close, vix ? 10 + (random.NextDouble() * 20) : null, 375, vix ? 375 : null));
            }

            date = date.AddDays(1);
        }

        return rows;
    }

    private static ContextFiguresAdapter Adapter(List<ContextDayRecord> rows) => new(new MemoryStore(rows), new ContextFiguresAdapter.Cache());

    private static async Task<RegimeLabel> LabelAsync(ContextFiguresAdapter adapter, DateOnly date) => RegimeLabel.Of(date, await adapter.AsOfAsync(date, Ct));

    [Fact]
    public void The_nine_names_and_codes_of_the_domain_are_the_ones_of_the_indicator_library()
    {
        for (var code = 1; code <= 9; code++)
        {
            RegimeNames.NameOf(code).ShouldBe(Regime.Name(code));
            RegimeNames.CodeOf(Regime.Name(code)).ShouldBe(code);
        }

        RegimeNames.All.Count.ShouldBe(9);
    }

    [Fact]
    public async Task A_label_exists_from_the_day_after_the_265th_stored_day_and_not_before_and_says_why()
    {
        var rows = Days(3, 300);
        var adapter = Adapter(rows);

        var early = await LabelAsync(adapter, rows[264].Date);
        var first = await LabelAsync(adapter, rows[265].Date);
        var before = await LabelAsync(adapter, rows[0].Date);

        (early.IsAvailable, early.Unavailable).ShouldBe((false, RegimeLabel.RegimeNotReady));
        first.IsAvailable.ShouldBeTrue();
        first.ContextAsOf.ShouldBe(rows[264].Date);
        (before.IsAvailable, before.Unavailable).ShouldBe((false, ContextRead.BeforeHistory));
        (await LabelAsync(Adapter([]), rows[10].Date)).Unavailable.ShouldBe(ContextRead.NoContext);
        (await LabelAsync(adapter, rows[^1].Date.AddDays(30))).Unavailable.ShouldBe(ContextRead.Stale);
    }

    [Fact]
    public async Task A_label_without_a_vix_percentile_is_built_from_the_index_alone_and_says_so()
    {
        var rows = Days(5, 300, vix: false);

        var label = await LabelAsync(Adapter(rows), rows[290].Date);

        label.IsAvailable.ShouldBeTrue();
        label.Basis.ShouldBe(ContextBasis.IndexOnly);
        label.CellPart.ShouldEndWith("~index_only");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task The_label_of_a_date_does_not_change_when_every_day_from_that_date_on_is_removed_or_changed(int seed)
    {
        var rows = Days(seed, 330);
        var full = Adapter(rows);
        var random = new Random(seed * 31);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var k = random.Next(266, 325);
            var date = rows[k].Date;
            var truncated = rows.Where(r => r.Date < date).ToList();
            var changed = rows.Select(r => r.Date < date ? r : r with { IndexClose = r.IndexClose, IndexHigh = r.IndexClose * 1.001, IndexLow = r.IndexClose * 0.999, VixClose = 80 }).ToList();

            var expected = await LabelAsync(full, date);

            (await LabelAsync(Adapter(truncated), date)).ShouldBe(expected, $"seed {seed}, {date}");
            (await LabelAsync(Adapter(changed), date)).ShouldBe(expected, $"seed {seed}, {date}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Appending_days_never_changes_an_earlier_label(int seed)
    {
        var rows = Days(seed, 330);
        var shorter = Adapter(rows[..300]);
        var longer = Adapter(rows);

        foreach (var row in rows.Skip(1).Take(299))
        {
            (await LabelAsync(longer, row.Date)).ShouldBe(await LabelAsync(shorter, row.Date), $"seed {seed}, {row.Date}");
        }
    }

    [Fact]
    public async Task The_batch_read_gives_the_same_answer_for_each_date_as_asking_one_at_a_time_and_reads_the_days_once()
    {
        var rows = Days(9, 320);
        var store = new MemoryStore(rows);
        var adapter = new ContextFiguresAdapter(store, new ContextFiguresAdapter.Cache());
        DateOnly[] dates = [rows[0].Date, rows[100].Date, rows[270].Date, rows[270].Date, rows[^1].Date, rows[^1].Date.AddDays(20), Start.AddDays(-5)];

        var many = await adapter.AsOfManyAsync(dates, Ct);

        many.Count.ShouldBe(dates.Distinct().Count());
        foreach (var date in dates)
        {
            many[date].ShouldBe(await adapter.AsOfAsync(date, Ct), date.ToString());
        }

        store.Lists.ShouldBe(1, "the stored days are read once for the whole batch and the cache holds them for the single reads");
        (await adapter.AsOfManyAsync([], Ct)).ShouldBeEmpty();
    }
}

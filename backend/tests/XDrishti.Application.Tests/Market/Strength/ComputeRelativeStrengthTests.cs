#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Strength;
using XDrishti.Application.Tests.Market.Context;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Strength;

/// <summary>BK-09 criteria 7 and 8 and the application tests of the plan: the daily step with dated membership and with a single snapshot, a rerun that inserts nothing, a share with an ex-date in its window excluded and counted, the sectors, and the index-relative row.</summary>
public sealed class ComputeRelativeStrengthTests
{
    private static readonly DateOnly Last = new(2026, 6, 30);

    private static readonly Guid BasketId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid IndexId = Guid.Parse("00000000-0000-7000-8000-0000000000a0");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryStore : IRelativeStrengthStore
    {
        public Dictionary<DateOnly, (List<StrengthRow> Rows, List<SectorRow> Sectors)> Days { get; } = [];

        public ValueTask<IReadOnlySet<DateOnly>> DatesWithRowsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<DateOnly>>(Days.Keys.Where(d => d >= from && d <= to).ToHashSet());

        public ValueTask<bool> InsertDayAsync(DateOnly asOf, IReadOnlyList<StrengthRow> rows, IReadOnlyList<SectorRow> sectors, CancellationToken cancellationToken)
        {
            if (Days.ContainsKey(asOf))
            {
                return ValueTask.FromResult(false);
            }

            Days[asOf] = ([.. rows], [.. sectors]);
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<StrengthRow>> ForInstrumentAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StrengthRow>>([.. Days.Values.SelectMany(d => d.Rows).Where(r => r.InstrumentId == instrumentId && r.AsOf >= from && r.AsOf <= to).OrderBy(r => r.AsOf)]);

        public ValueTask<IReadOnlyList<SectorRow>> SectorRowsAsync(string sector, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<SectorRow>>([.. Days.Values.SelectMany(d => d.Sectors).Where(r => r.Sector == sector && r.AsOf >= from && r.AsOf <= to).OrderBy(r => r.AsOf)]);

        public ValueTask<IReadOnlyList<StrengthRow>> ListAsync(Guid? basketId, DateOnly? asOf, int take, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StrengthRow>>([.. Days.Values.SelectMany(d => d.Rows).Take(take)]);
    }

    private static List<DateOnly> Sessions(int count)
    {
        var list = new List<DateOnly>();
        for (var d = Last; list.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                list.Insert(0, d);
            }
        }

        return list;
    }

    private static BarRow Bar(DateOnly date, double close) =>
        new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset), close, close, close, close, 1000, 375, true, null);

    private static IReadOnlyList<BarRow> Closes(int index, int sessions = 300) =>
        [.. Sessions(sessions).Select((d, t) => Bar(d, 100 * Math.Pow(1 + (0.0005 * (index + 1)), t) * (1 + (0.01 * Math.Sin(t * (index + 2))))))];

    private static BasketMemberView Member(Guid id, string symbol, string sector) =>
        new(Guid.NewGuid(), new InstrumentSummary(id, symbol, symbol, symbol, "share", null, null, sector, 1, null, null, false, false, false, TrackedInfo.Never), 0, DateTimeOffset.UnixEpoch, null, null, "test", null, null);

    private sealed class Rig
    {
        public Rig(int members, bool enabled = true, bool dated = false, DateOnly? exDate = null, int exIndex = -1, bool withIndex = true, int catchUp = 1)
        {
            Settings = new RelativeStrengthSettings { Enabled = enabled, CatchUpSessions = catchUp };
            var ids = Enumerable.Range(0, members).Select(i => Guid.Parse($"00000000-0000-7000-8000-{i + 100:D12}")).ToList();
            var items = ids.Select((id, i) => Member(id, "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), i % 2 == 0 ? "Banks" : "IT")).ToList();
            var baskets = Substitute.For<IBasketQueries>();
            baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BasketView>>(
                [new BasketView(BasketId, "NSE Nifty 100", "#111111", "watch", null, 0, members, DateTimeOffset.UnixEpoch, null, "index", true, null, null)]));
            baskets.MembersAsync(BasketId, Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                .Returns(call => ValueTask.FromResult<BasketMembers?>(new BasketMembers(BasketId, "NSE Nifty 100", null, call.ArgAt<DateOnly?>(2), items.Count, items)));
            var at = DateTimeOffset.UnixEpoch;
            var entries = dated
                ? new[] { new BasketHistoryEntry(Guid.NewGuid(), "added", at, ids[0], "S00", "S00", "share", null, null), new BasketHistoryEntry(Guid.NewGuid(), "added", at.AddDays(40), ids[1], "S01", "S01", "share", null, null) }
                : new[] { new BasketHistoryEntry(Guid.NewGuid(), "added", at, ids[0], "S00", "S00", "share", null, null), new BasketHistoryEntry(Guid.NewGuid(), "added", at.AddMinutes(5), ids[1], "S01", "S01", "share", null, null) };
            baskets.HistoryAsync(BasketId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketHistory?>(new BasketHistory(BasketId, "NSE Nifty 100", entries)));

            var bars = Substitute.For<IBarQueries>();
            for (var i = 0; i < members; i++)
            {
                var rows = Closes(i);
                bars.ReadDailyBarsAsync(ids[i], Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>(rows));
            }

            bars.ReadDailyBarsAsync(IndexId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>(Closes(5)));

            var actions = Substitute.For<ICorporateActionQueries>();
            IReadOnlySet<ActionKey> keys = exDate is { } ex && exIndex >= 0 ? new HashSet<ActionKey> { new(ids[exIndex], ex, CorporateActionKind.Split) } : new HashSet<ActionKey>();
            actions.ListKeysAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(keys));

            var context = new FakeContextBarSource { Legs = new ContextLegs(withIndex ? new ContextInstrument(IndexId, "NSE:NIFTY") : null, null, [], []) };
            var calendar = Substitute.For<ITradingCalendar>();
            calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
            Handler = new ComputeRelativeStrengthHandler(Settings, new FakeContextSettings(), context, baskets, bars, actions, Store, calendar, new FakeTimeProvider(new DateTimeOffset(2026, 6, 30, 14, 30, 0, TimeSpan.Zero)), NullLogger<ComputeRelativeStrengthHandler>.Instance);
        }

        public RelativeStrengthSettings Settings { get; }

        public MemoryStore Store { get; } = new();

        public ComputeRelativeStrengthHandler Handler { get; }

        public async Task<RelativeStrengthComputed> RunAsync(ComputeRelativeStrengthCommand? command = null)
        {
            var result = await Handler.HandleAsync(command ?? new ComputeRelativeStrengthCommand(), Ct);
            result.IsSuccess.ShouldBeTrue();
            return result.Value;
        }
    }

    [Fact]
    public async Task A_basket_with_one_snapshot_is_ranked_and_the_row_says_single_snapshot()
    {
        var rig = new Rig(12);

        var done = await rig.RunAsync();

        done.Written.ShouldBe([Last]);
        var rows = rig.Store.Days[Last].Rows.Where(r => r.BasketId == BasketId && r.Lookback == 126).ToList();
        rows.Count.ShouldBe(12);
        rows.ShouldAllBe(r => r.State == "value" && r.MembershipBasis == "single_snapshot" && r.NRanked == 12 && r.Skip == 21);
        rows.Max(r => r.PctRank).ShouldBe(1.0);
        rows.Min(r => r.PctRank).ShouldBe(0.0);
    }

    [Fact]
    public async Task A_basket_with_changes_on_two_days_is_dated()
    {
        var rig = new Rig(12, dated: true);

        await rig.RunAsync();

        rig.Store.Days[Last].Rows.Where(r => r.BasketId == BasketId).ShouldAllBe(r => r.MembershipBasis == "dated");
    }

    [Fact]
    public async Task A_rerun_inserts_nothing_and_says_what_exists()
    {
        var rig = new Rig(12);
        await rig.RunAsync();
        var before = rig.Store.Days[Last].Rows.Count;

        var again = await rig.RunAsync();

        again.Written.ShouldBeEmpty();
        again.Existing.ShouldBe([Last]);
        again.Rows.ShouldBe(0);
        rig.Store.Days[Last].Rows.Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_range_writes_each_missing_session_once_and_leaves_the_ones_that_exist()
    {
        var rig = new Rig(12);
        await rig.RunAsync();

        var done = await rig.RunAsync(new ComputeRelativeStrengthCommand(Last.AddDays(-4), Last));

        done.Existing.ShouldBe([Last]);
        done.Written.ShouldBe([Last.AddDays(-4), Last.AddDays(-1)], "Friday and Monday; the weekend is no session");
    }

    [Fact]
    public async Task A_share_with_an_ex_date_in_its_window_is_excluded_and_counted_never_repaired()
    {
        var rig = new Rig(12, exDate: Last.AddDays(-60), exIndex: 3);

        await rig.RunAsync();

        var rows = rig.Store.Days[Last].Rows.Where(r => r.BasketId == BasketId && r.Lookback == 126).ToList();
        var excluded = rows.Single(r => r.InstrumentId == Guid.Parse("00000000-0000-7000-8000-000000000103"));
        excluded.State.ShouldBe("corporate_action_unresolved");
        excluded.Score.ShouldBeNull();
        excluded.PctRank.ShouldBeNull();
        rows.Where(r => r.State == "value").ShouldAllBe(r => r.NRanked == 11);
    }

    [Fact]
    public async Task Fewer_than_ten_voters_leave_every_rank_insufficient()
    {
        var rig = new Rig(9);

        await rig.RunAsync();

        rig.Store.Days[Last].Rows.Where(r => r.BasketId == BasketId && r.Lookback == 126).ShouldAllBe(r => r.State == "insufficient" && r.PctRank == null);
    }

    [Fact]
    public async Task The_sectors_are_ranked_from_the_members_and_a_sector_below_the_minimum_has_no_rank()
    {
        var rig = new Rig(12);

        await rig.RunAsync();

        var sectors = rig.Store.Days[Last].Sectors.Where(s => s.WindowSessions == 63).ToList();
        sectors.Count.ShouldBe(2);
        sectors.ShouldAllBe(s => s.State == "value" && s.NMembers == 6);
        sectors.Select(s => s.Rank).Order().ShouldBe([1, 2]);

        var small = new Rig(6);
        await small.RunAsync();
        small.Store.Days[Last].Sectors.ShouldAllBe(s => s.State == "insufficient" && s.Rank == null);
    }

    [Fact]
    public async Task The_index_relative_row_is_written_per_share_and_is_unavailable_without_the_index()
    {
        var with = new Rig(12);
        var without = new Rig(12, withIndex: false);

        await with.RunAsync();
        await without.RunAsync();

        var rows = with.Store.Days[Last].Rows.Where(r => r.Lookback == 20).ToList();
        rows.Count.ShouldBe(12);
        rows.ShouldAllBe(r => r.State == "value" && r.BasketId == Guid.Empty && r.Score != null && r.MembershipBasis == "none");
        without.Store.Days[Last].Rows.Where(r => r.Lookback == 20).ShouldAllBe(r => r.State == "unavailable" && r.Score == null);
    }

    [Fact]
    public async Task The_step_is_off_by_default_and_writes_nothing()
    {
        new RelativeStrengthSettings().Enabled.ShouldBeFalse();
        var rig = new Rig(12, enabled: false);

        var done = await rig.RunAsync();

        done.Missing.ShouldBe("disabled");
        rig.Store.Days.ShouldBeEmpty();
    }

    [Fact(Explicit = true)]
    public async Task The_step_for_a_500_member_basket_and_twelve_sectors_takes_under_four_seconds()
    {
        var rig = new Rig(500);
        var started = System.Diagnostics.Stopwatch.StartNew();

        await rig.RunAsync();

        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4));
    }
}

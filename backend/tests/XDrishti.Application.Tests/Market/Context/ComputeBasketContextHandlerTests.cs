#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Tests.Selection;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>BK-08 criteria 7 and 8 through the daily upkeep of the gate basket (fake ports): one row per missing session, insert-once, membership as of each day, the basis stated, the last session waiting for every bar.</summary>
public sealed class ComputeBasketContextHandlerTests
{
    private static readonly Guid Basket = new("00000000-0000-0000-0000-0000000000b1");

    private static readonly DateOnly Mar30 = new(2026, 3, 30);

    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryContextStore : IBasketContextStore
    {
        public Dictionary<(Guid, DateOnly), BasketContextRow> Rows { get; } = [];

        public ValueTask<bool> InsertOnceAsync(BasketContextRow row, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.TryAdd((row.BasketId, row.AsOf), row));

        public ValueTask<BasketContextRow?> LatestBeforeAsync(Guid basketId, DateOnly beforeExclusive, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.Values.Where(r => r.BasketId == basketId && r.AsOf < beforeExclusive).OrderByDescending(r => r.AsOf).FirstOrDefault());

        public ValueTask<IReadOnlySet<DateOnly>> DatesAsync(Guid basketId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<DateOnly>>(Rows.Values.Where(r => r.BasketId == basketId && r.AsOf >= from && r.AsOf <= to).Select(r => r.AsOf).ToHashSet());

        public ValueTask<IReadOnlyList<BasketContextRow>> ListAsync(Guid basketId, DateOnly? from, DateOnly? to, int take, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<BasketContextRow>>([.. Rows.Values.Where(r => r.BasketId == basketId && (from is null || r.AsOf >= from) && (to is null || r.AsOf <= to)).OrderByDescending(r => r.AsOf).Take(take)]);
    }

    private sealed class Harness
    {
        private readonly Guid[] _instruments = [.. Enumerable.Range(0, 10).Select(i => new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]))];

        public MemoryContextStore Store { get; } = new();

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 30, 11, 0, 0, TimeSpan.Zero)); // 16:30 India time: the 30th has closed

        /// <summary>Instruments (by index) that are members on a given day.</summary>
        public Func<DateOnly, IEnumerable<int>> MembersOn { get; set; } = _ => Enumerable.Range(0, 10);

        /// <summary>Instruments (by index) that have no bar on the 31st.</summary>
        public HashSet<int> NoBarOn31 { get; } = [];

        public DateOnly? KnownSince { get; set; }

        public BasketContextRow Row(DateOnly day) => Store.Rows[(Basket, day)];

        public ComputeBasketContextHandler Handler(BasketSettings? settings = null, bool basketExists = true)
        {
            var queries = Substitute.For<IBasketQueries>();
            queries.MembersAsync(Basket, Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var day = call.ArgAt<DateOnly?>(2)!.Value;
                var items = MembersOn(day).Select((i, p) => new BasketMemberView(Guid.NewGuid(), Summary(i), p, DateTimeOffset.UnixEpoch, null, null, "owner", null, null)).ToList();
                return ValueTask.FromResult(basketExists ? new BasketMembers(Basket, "Nifty 50", null, day, items.Count, items, KnownSince) : null);
            });
            var bars = Substitute.For<IBarQueries>();
            bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var index = Array.IndexOf(_instruments, call.ArgAt<Guid>(0));
                return ValueTask.FromResult<IReadOnlyList<BarRow>>([.. BarsOf(index)]);
            });
            var calendar = Substitute.For<ITradingCalendar>();
            calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(new TradingCalendar()));
            return new ComputeBasketContextHandler(new FakeBasketSettingsStore(settings ?? BasketSettings.Defaults with { BreadthSource = BasketSettings.SourceOf(Basket) }), queries, bars, Store, calendar, Clock, NullLogger<ComputeBasketContextHandler>.Instance);
        }

        private InstrumentSummary Summary(int i) => new(_instruments[i], "M" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "n", "n", "share", null, null, null, 1, null, null, false, false, false, TrackedInfo.Never);

        // 40 weekday bars ending on the 31st: even instruments rise, odd ones fall, so five are above their 20-day average.
        private IEnumerable<BarRow> BarsOf(int index)
        {
            var days = new List<DateOnly>();
            for (var d = Mar31; days.Count < 40; d = d.AddDays(-1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    days.Add(d);
                }
            }

            days.Reverse();
            for (var k = 0; k < days.Count; k++)
            {
                if (days[k] == Mar31 && NoBarOn31.Contains(index))
                {
                    continue;
                }

                var close = index % 2 == 0 ? 100 + k + (0.1 * index) : 140 - k + (0.1 * index);
                yield return new BarRow(new DateTimeOffset(days[k].Year, days[k].Month, days[k].Day, 9, 15, 0, TimeSpan.FromHours(5.5)), close, close, close, close, 1000, 375, true, "test");
            }
        }
    }

    [Fact]
    public async Task With_the_gate_reading_the_tracked_set_nothing_is_computed()
    {
        var h = new Harness();

        var result = await h.Handler(BasketSettings.Defaults).HandleAsync(new ComputeBasketContextCommand(), Ct);

        result.Value.ShouldBe(new BasketContextComputed(0, 0, ComputeBasketContextHandler.NoGateBasket));
        h.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_chosen_basket_that_no_longer_exists_writes_nothing_and_says_so()
    {
        var h = new Harness();

        var result = await h.Handler(basketExists: false).HandleAsync(new ComputeBasketContextCommand(), Ct);

        result.Value.Skipped.ShouldBe(ComputeBasketContextHandler.BasketMissing);
        h.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_of_the_latest_sessions_without_a_row_gets_one_with_voters_breadth_and_the_basis_stated()
    {
        var h = new Harness();

        var result = await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        result.Value.Written.ShouldBe(ComputeBasketContextHandler.RecentSessions);
        var row = h.Row(Mar30);
        (row.NMembers, row.NVoters, row.B20, row.State).ShouldBe((10, 10, 0.5, "value")); // five rising members are above their average
        row.MembershipBasis.ShouldBe("single_snapshot"); // a basket with no dated membership is never presented as point in time
    }

    [Fact]
    public async Task A_basket_with_dated_membership_since_before_the_day_is_stored_as_point_in_time()
    {
        var h = new Harness { KnownSince = new DateOnly(2026, 3, 1) };

        await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        h.Row(Mar30).MembershipBasis.ShouldBe("point_in_time");
        var early = new Harness { KnownSince = Mar30 };
        await early.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);
        early.Row(Mar30).MembershipBasis.ShouldBe("point_in_time");
        early.Row(new DateOnly(2026, 3, 26)).MembershipBasis.ShouldBe("single_snapshot");
    }

    [Fact]
    public async Task A_second_run_for_the_same_dates_inserts_nothing_and_changes_no_row()
    {
        var h = new Harness();
        await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);
        var before = h.Store.Rows.ToDictionary(p => p.Key, p => p.Value);

        var again = await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        again.Value.Written.ShouldBe(0);
        h.Store.Rows.ShouldBe(before);
    }

    [Fact]
    public async Task The_last_session_waits_until_every_member_has_its_bar_and_is_written_by_a_later_run()
    {
        var h = new Harness();
        h.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 31, 11, 0, 0, TimeSpan.Zero));
        h.NoBarOn31.Add(3);

        var first = await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        first.Value.Pending.ShouldBe(1);
        h.Store.Rows.Keys.ShouldNotContain((Basket, Mar31));
        h.Store.Rows.Keys.ShouldContain((Basket, Mar30));

        h.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero));
        var later = await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        later.Value.Pending.ShouldBe(1); // the new last session (April 1) has no bars yet
        h.Row(Mar31).NVoters.ShouldBe(9); // the 31st is no longer the last session: written with what exists, the member without a bar does not vote
    }

    [Fact]
    public async Task A_member_that_leaves_on_day_two_changes_day_two_and_never_day_one()
    {
        var h = new Harness();
        await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);
        var dayOne = h.Row(Mar30);
        h.MembersOn = day => day >= Mar31 ? Enumerable.Range(0, 9) : Enumerable.Range(0, 10);
        h.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 31, 11, 0, 0, TimeSpan.Zero));

        var result = await h.Handler().HandleAsync(new ComputeBasketContextCommand(), Ct);

        result.Value.Written.ShouldBe(1);
        h.Row(Mar30).ShouldBe(dayOne);
        (h.Row(Mar31).NMembers, h.Row(Mar31).NVoters).ShouldBe((9, 9));
    }
}

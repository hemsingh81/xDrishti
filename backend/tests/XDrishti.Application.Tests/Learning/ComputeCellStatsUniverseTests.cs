using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>BK-11 (ADR 0261): the universe split of the cell statistics. The tier of a trade comes from daily bars before its decision date, a trade with no tier is counted and stays in the <c>*</c> cell, and the <c>*</c> rows do not change.</summary>
public sealed class ComputeCellStatsUniverseTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _store = Substitute.For<ICellStatsStore>();

    private readonly IContextFigures _figures = Substitute.For<IContextFigures>();

    private readonly IUniverseSeriesSource _source = Substitute.For<IUniverseSeriesSource>();

    private CellSnapshotToStore? _written;

    private static Guid Share(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    private ComputeCellStatsHandler Handler(bool withSource = true)
    {
        _store.StoreAsync(Arg.Do<CellSnapshotToStore>(s => _written = s), Ct).Returns(true);
        return new ComputeCellStatsHandler(_runs, _store, _figures, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance, universeSource: withSource ? _source : null);
    }

    /// <summary>Shares 1 to 35, each with a daily session from 2025-11-01 up to the day before <paramref name="until"/>; the share with the higher number trades less, so share 1 is the most liquid (the top third) and share 35 the least.</summary>
    private void Series(DateOnly until, int shares = 35)
    {
        _source.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var to = call.ArgAt<DateOnly>(1);
            var map = new Dictionary<Guid, IReadOnlyList<SessionValue>>();
            for (var i = 1; i <= shares; i++)
            {
                map[Share(i)] = [.. Enumerable.Range(0, 200).Select(d => new DateOnly(2025, 11, 1).AddDays(d)).TakeWhile(d => d < to && d < until).Select(d => new SessionValue(d, 1000 - i))];
            }

            return (IReadOnlyDictionary<Guid, IReadOnlyList<SessionValue>>)map;
        });
    }

    private static StoredRunHeader Header(string key) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private void Run(params (int Share, DateOnly Date, double R)[] trades)
    {
        _runs.FindAsync(KeyA, Ct).Returns(Header(KeyA));
        _runs.StreamTradesAsync(KeyA, false, Ct).Returns(Stream([.. trades.Select(t => FabricatedRun.Trade(Share(t.Share), "NSE:S" + t.Share, Side.Buy, t.Date, rFilled: t.R, rExact: t.R))]));
    }

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static ComputeCellStatsCommand Command(bool byUniverse) => new(AsOf, [new CellSource(KeyA, "intraday", "FIXED_RR")], new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 0, MinTrades: 2), "test", ByUniverse: byUniverse);

    private static readonly DateOnly Day = new(2026, 3, 2);

    [Fact]
    public async Task Trades_go_to_the_tier_of_their_share_on_the_decision_date_and_the_star_rows_do_not_change()
    {
        Series(AsOf);
        Run((1, Day, 1), (2, Day.AddDays(1), -1), (18, Day.AddDays(2), 2), (35, Day.AddDays(3), -2), (34, Day.AddDays(4), 1));
        var plain = await Handler().HandleAsync(Command(false), Ct);
        var plainRows = _written!.Rows.ToList();

        var result = await Handler().HandleAsync(Command(true), Ct);

        result.IsSuccess.ShouldBeTrue();
        var keys = _written!.Rows.Select(r => r.Cell.Key).Distinct().Order().ToList();
        keys.ShouldBe(
        [
            "ORB|15m|BUY|intraday|FIXED_RR|*",
            "ORB|15m|BUY|intraday|FIXED_RR|*|liq_high",
            "ORB|15m|BUY|intraday|FIXED_RR|*|liq_low",
            "ORB|15m|BUY|intraday|FIXED_RR|*|liq_mid",
        ]);
        _written.Rows.Where(r => r.Cell.Universe == "*").ToList().ShouldBe(plainRows);
        _written.Rows.Single(r => r.Cell.Universe == "liq_high" && r.Window == CellWindow.All).N.ShouldBe(2);   // shares 1 and 2
        _written.Rows.Single(r => r.Cell.Universe == "liq_mid" && r.Window == CellWindow.All).N.ShouldBe(1);    // share 18
        _written.Rows.Single(r => r.Cell.Universe == "liq_low" && r.Window == CellWindow.All).N.ShouldBe(2);    // shares 34 and 35
        result.Value.Universe.ShouldBe(new UniverseTally(3, 5, 0, 0));
        result.Value.SnapshotKey.ShouldNotBe(plain.Value.SnapshotKey);
        _written.Rows.Where(r => r.Cell.Universe != "*").ShouldAllBe(r => r.Shrinkage == null);
    }

    [Fact]
    public async Task The_source_is_read_only_up_to_the_last_decision_date_and_a_later_bar_changes_nothing()
    {
        Series(AsOf);
        Run((1, Day, 1), (2, Day.AddDays(1), -1), (35, Day.AddDays(2), 2));
        await Handler().HandleAsync(Command(true), Ct);
        var first = _written!.Rows.Select(r => r.Cell.Key + "|" + r.N).ToList();

        await _source.Received().ReadAsync(Arg.Any<DateOnly>(), Day.AddDays(2), Arg.Any<CancellationToken>());

        Series(AsOf.AddDays(20));   // later sessions appear in the store
        await Handler().HandleAsync(Command(true), Ct);

        _written!.Rows.Select(r => r.Cell.Key + "|" + r.N).ShouldBe(first);
    }

    [Fact]
    public async Task A_trade_with_no_dated_set_or_too_few_bars_is_counted_unknown_and_stays_in_the_star_cell_only()
    {
        Series(AsOf);
        Run((1, new DateOnly(2025, 11, 3), 1), (2, new DateOnly(2025, 11, 4), -1));   // fewer than 60 sessions before these days

        var result = await Handler().HandleAsync(Command(true), Ct);

        result.Value.Universe.ShouldBe(new UniverseTally(0, 0, 2, 0));
        _written!.Rows.Select(r => r.Cell.Key).Distinct().ShouldBe(["ORB|15m|BUY|intraday|FIXED_RR|*"]);
    }

    [Fact]
    public async Task With_fewer_than_thirty_shares_every_trade_is_unavailable_and_no_tier_is_invented()
    {
        Series(AsOf, shares: 20);
        Run((1, Day, 1), (2, Day.AddDays(1), -1));

        var result = await Handler().HandleAsync(Command(true), Ct);

        result.Value.Universe.ShouldBe(new UniverseTally(0, 0, 0, 2));
    }

    [Fact]
    public async Task The_universe_split_without_a_tier_source_is_refused_before_any_write()
    {
        Run((1, Day, 1));

        var result = await Handler(withSource: false).HandleAsync(Command(true), Ct);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceive().StoreAsync(Arg.Any<CellSnapshotToStore>(), Arg.Any<CancellationToken>());
    }
}

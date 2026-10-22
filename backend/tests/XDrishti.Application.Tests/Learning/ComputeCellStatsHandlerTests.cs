using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The cell statistics use case (spec P6-01): cells from stored runs, the point-in-time cut, idempotence, and the refusals before any read.</summary>
public sealed class ComputeCellStatsHandlerTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly string KeyB = new('b', 64);

    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _store = Substitute.For<ICellStatsStore>();

    private CellSnapshotToStore? _written;

    private ComputeCellStatsHandler Handler()
    {
        _store.StoreAsync(Arg.Do<CellSnapshotToStore>(s => _written = s), Ct).Returns(true);
        return new ComputeCellStatsHandler(_runs, _store, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance);
    }

    private static StoredRunHeader Header(string key) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static SimTrade Trade(Side side, DateOnly date, double r, string strategy = "ORB") =>
        FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", side, date, rFilled: r, rExact: r, strategy: strategy);

    private void Run(string key, params SimTrade[] trades)
    {
        _runs.FindAsync(key, Ct).Returns(Header(key));
        _runs.StreamTradesAsync(key, false, Ct).Returns(Stream(trades));
    }

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static ComputeCellStatsCommand Command(params CellSource[] sources) => new(AsOf, sources, new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 0, MinTrades: 2), "test");

    [Fact]
    public async Task Cells_are_made_per_strategy_timeframe_and_side_with_the_labels_of_the_source_and_a_later_exit_is_left_out()
    {
        Run(KeyA, Trade(Side.Buy, AsOf, 1), Trade(Side.Buy, AsOf.AddDays(-30), -1), Trade(Side.Sell, AsOf, 2), Trade(Side.Buy, AsOf.AddDays(1), -9));

        var result = await Handler().HandleAsync(Command(new CellSource(KeyA, "intraday", "FIXED_RR")), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Cells.ShouldBe(2);
        result.Value.Rows.ShouldBe(6);
        result.Value.Trades.ShouldBe(4);
        result.Value.AlreadyStored.ShouldBeFalse();
        _written!.Rows.Select(r => r.Cell.Key).Distinct().Order().ShouldBe(["ORB|15m|BUY|intraday|FIXED_RR|*", "ORB|15m|SELL|intraday|FIXED_RR|*"]);
        var buyAll = _written.Rows.Single(r => r.Cell.Side == "BUY" && r.Window == CellWindow.All);
        buyAll.N.ShouldBe(2);
        buyAll.ExpectancyR!.Value.ShouldBe((1 - 0.5) / 1.5, 1e-12);
        buyAll.Costed.ShouldBeFalse();
        _written.CreatedBy.ShouldBe("test");
        _written.SnapshotKey.Length.ShouldBe(64);
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_key_and_a_stored_snapshot_is_reported_as_already_stored()
    {
        Run(KeyA, Trade(Side.Buy, AsOf, 1));
        var handler = Handler();
        var first = await handler.HandleAsync(Command(new CellSource(KeyA, "swing", "ATR")), Ct);
        _store.StoreAsync(Arg.Any<CellSnapshotToStore>(), Ct).Returns(false);
        Run(KeyA, Trade(Side.Buy, AsOf, 1));

        var second = await handler.HandleAsync(Command(new CellSource(KeyA, "swing", "ATR")), Ct);
        var other = await handler.HandleAsync(Command(new CellSource(KeyA, "swing", "FIXED")), Ct);

        second.Value.SnapshotKey.ShouldBe(first.Value.SnapshotKey);
        second.Value.AlreadyStored.ShouldBeTrue();
        other.Value.SnapshotKey.ShouldNotBe(first.Value.SnapshotKey);
    }

    [Fact]
    public async Task Two_runs_that_make_the_same_cell_are_refused()
    {
        Run(KeyA, Trade(Side.Buy, AsOf, 1));
        Run(KeyB, Trade(Side.Buy, AsOf, 1));

        var result = await Handler().HandleAsync(Command(new CellSource(KeyA, "intraday", "X"), new CellSource(KeyB, "intraday", "X")), Ct);

        result.Error.Code.ShouldBe(LearningErrors.CellDuplicateCode);
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, Ct);
    }

    [Fact]
    public async Task Runs_of_two_strategies_make_two_cells_each_shrunk_toward_its_own_strategy()
    {
        Run(KeyA, Trade(Side.Buy, AsOf, 1));
        Run(KeyB, Trade(Side.Buy, AsOf, -1, "VWAP_PULLBACK"));
        var command = Command(new CellSource(KeyA, "intraday", "X"), new CellSource(KeyB, "intraday", "X")) with { Options = new CellStatsOptions(30, 2, 1, 0.9) };

        var result = await Handler().HandleAsync(command, Ct);

        result.Value.Cells.ShouldBe(2);
        var priors = _written!.Rows.Where(r => r.Window == CellWindow.All).OrderBy(r => r.Cell.Key, StringComparer.Ordinal).Select(r => r.PriorRate).ToList();
        priors[0].ShouldBe(2.0 / 3, 1e-12); // ORB: (1 + 2 x 0.5) / (1 + 2)
        priors[1].ShouldBe(1.0 / 3, 1e-12); // VWAP_PULLBACK: (0 + 2 x 0.5) / (1 + 2)
    }

    [Fact]
    public async Task A_run_that_is_not_stored_is_not_found()
    {
        _runs.FindAsync(KeyA, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(Command(new CellSource(KeyA, "intraday", "X")), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
        await _store.DidNotReceiveWithAnyArgs().StoreAsync(default!, Ct);
    }

    [Theory]
    [InlineData("nothex", "intraday", "X")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "monthly", "X")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "swing", "a|b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "swing", "")]
    public async Task A_bad_source_is_refused_before_anything_is_read(string key, string style, string exit)
    {
        var result = await Handler().HandleAsync(Command(new CellSource(key, style, exit)), Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task No_source_a_repeated_run_and_bad_options_are_refused()
    {
        var none = await Handler().HandleAsync(Command(), Ct);
        var twice = await Handler().HandleAsync(Command(new CellSource(KeyA, "swing", "X"), new CellSource(KeyA, "intraday", "X")), Ct);
        var bad = await Handler().HandleAsync(Command(new CellSource(KeyA, "swing", "X")) with { Options = new CellStatsOptions(0) }, Ct);

        none.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        twice.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }
}

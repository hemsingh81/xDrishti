using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>P6-02 (ADR 0146): the regime split of the cell statistics. The regime of a trade is read before its entry day, the two bases are two cells, an unlabelled trade is counted, and the <c>*</c> rows do not change.</summary>
public sealed class ComputeCellStatsRegimeTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _store = Substitute.For<ICellStatsStore>();

    private readonly IContextFigures _figures = Substitute.For<IContextFigures>();

    private CellSnapshotToStore? _written;

    private ComputeCellStatsHandler Handler(Func<DateOnly, ContextRead> script)
    {
        _store.StoreAsync(Arg.Do<CellSnapshotToStore>(s => _written = s), Ct).Returns(true);
        _figures.AsOfManyAsync(Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<DateOnly, ContextRead>)ci.Arg<IReadOnlyCollection<DateOnly>>().ToDictionary(d => d, script));
        return new ComputeCellStatsHandler(_runs, _store, _figures, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance);
    }

    private static ContextRead Read(DateOnly date, int code, string basis) =>
        ContextRead.Of(new ContextFigures(date.AddDays(-1), basis, 1, 20, 50, 15, 40, 0, 1, 0, code, null));

    private static StoredRunHeader Header(string key) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private void Run(params (DateOnly Date, double R)[] trades)
    {
        _runs.FindAsync(KeyA, Ct).Returns(Header(KeyA));
        _runs.StreamTradesAsync(KeyA, false, Ct).Returns(Stream([.. trades.Select(t => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, t.Date, rFilled: t.R, rExact: t.R))]));
    }

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static ComputeCellStatsCommand Command(bool byRegime) =>
        new(AsOf, [new CellSource(KeyA, "intraday", "FIXED_RR")], new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 5, MinTrades: 2), "test", byRegime);

    private static ContextRead Script(DateOnly d) =>
        d == AsOf ? Read(d, 1, ContextBasis.IndexAndVix)
        : d == AsOf.AddDays(-1) ? Read(d, 1, ContextBasis.IndexAndVix)
        : d == AsOf.AddDays(-2) ? Read(d, 9, ContextBasis.IndexOnly)
        : ContextRead.Not(ContextRead.BeforeHistory);

    [Fact]
    public async Task Without_the_flag_no_regime_is_read_and_the_key_is_the_one_it_always_was()
    {
        Run((AsOf, 1), (AsOf.AddDays(-1), -1));

        var result = await Handler(Script).HandleAsync(Command(false), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Regime.ShouldBeNull();
        await _figures.DidNotReceiveWithAnyArgs().AsOfManyAsync(default!, Ct);
        _written!.Rows.ShouldAllBe(r => r.Cell.Regime == "*");
        result.Value.SnapshotKey.ShouldBe(ComputeCellStatsHandler.KeyOf(AsOf, new CellStatsOptions(30, 5, 2, 0.9), $"{KeyA}:intraday:FIXED_RR"));
    }

    [Fact]
    public async Task With_the_flag_the_trades_are_split_by_the_regime_of_the_day_before_the_entry_and_the_two_bases_are_two_cells()
    {
        Run((AsOf, 1), (AsOf.AddDays(-1), 2), (AsOf.AddDays(-2), -1), (AsOf.AddDays(-3), 3));

        var result = await Handler(Script).HandleAsync(Command(true), Ct);

        result.IsSuccess.ShouldBeTrue();
        var all = _written!.Rows.Where(r => r.Window == CellWindow.All).ToDictionary(r => r.Cell.Regime, r => r);
        all.Keys.Order(StringComparer.Ordinal).ShouldBe(["*", "DOWN_HIGH_VOL~index_only", "UP_LOW_VOL"]);
        all["*"].N.ShouldBe(4);
        all["UP_LOW_VOL"].N.ShouldBe(2);
        all["UP_LOW_VOL"].Enough.ShouldBeTrue();
        all["DOWN_HIGH_VOL~index_only"].N.ShouldBe(1);
        all["DOWN_HIGH_VOL~index_only"].Enough.ShouldBeFalse("one trade is under the minimum of two");
        var tally = result.Value.Regime!;
        tally.Labelled.ShouldBe(3);
        tally.IndexAndVix.ShouldBe(2);
        tally.IndexOnly.ShouldBe(1);
        tally.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["before_history"] = 1 });
        (all["UP_LOW_VOL"].N + all["DOWN_HIGH_VOL~index_only"].N + tally.Unavailable).ShouldBe(all["*"].N, "the regime cells and the unlabelled trades are exactly the trades of the cell");
        result.Value.RegimeCells.ShouldBe(2);
    }

    [Fact]
    public async Task The_star_rows_are_the_same_with_and_without_the_split_and_the_key_differs()
    {
        Run((AsOf, 1), (AsOf.AddDays(-1), 2), (AsOf.AddDays(-2), -1), (AsOf.AddDays(-3), 3));
        var without = await Handler(Script).HandleAsync(Command(false), Ct);
        var starWithout = _written!.Rows.ToList();

        Run((AsOf, 1), (AsOf.AddDays(-1), 2), (AsOf.AddDays(-2), -1), (AsOf.AddDays(-3), 3));
        var with = await Handler(Script).HandleAsync(Command(true), Ct);
        var starWith = _written!.Rows.Where(r => r.Cell.Regime == "*").ToList();

        starWith.ShouldBe(starWithout);
        with.Value.SnapshotKey.ShouldNotBe(without.Value.SnapshotKey);
    }

    [Fact]
    public async Task A_changed_label_gives_a_new_snapshot_and_the_same_labels_the_same_one()
    {
        Run((AsOf, 1), (AsOf.AddDays(-1), 2));
        var first = await Handler(Script).HandleAsync(Command(true), Ct);
        Run((AsOf, 1), (AsOf.AddDays(-1), 2));
        var same = await Handler(Script).HandleAsync(Command(true), Ct);
        Run((AsOf, 1), (AsOf.AddDays(-1), 2));
        var other = await Handler(d => d == AsOf ? Read(d, 5, ContextBasis.IndexAndVix) : Script(d)).HandleAsync(Command(true), Ct);

        same.Value.SnapshotKey.ShouldBe(first.Value.SnapshotKey);
        other.Value.SnapshotKey.ShouldNotBe(first.Value.SnapshotKey);
    }

    [Fact]
    public async Task With_no_context_at_all_there_are_no_regime_cells_and_every_trade_is_counted_by_the_reason()
    {
        Run((AsOf, 1), (AsOf.AddDays(-1), 2));

        var result = await Handler(_ => ContextRead.Not(ContextRead.NoContext)).HandleAsync(Command(true), Ct);

        result.IsSuccess.ShouldBeTrue();
        _written!.Rows.ShouldAllBe(r => r.Cell.Regime == "*");
        result.Value.RegimeCells.ShouldBe(0);
        result.Value.Regime!.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["no_context"] = 2 });
    }
}

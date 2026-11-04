using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-02: the compute handler writes the shrunk figures on every row, keeps the key of a snapshot without them, and reports the floor count.</summary>
public sealed class ComputeCellStatsShrinkageTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _store = Substitute.For<ICellStatsStore>();

    private readonly IContextFigures _figures = Substitute.For<IContextFigures>();

    private CellSnapshotToStore? _written;

    private ComputeCellStatsHandler Handler(ShrinkageOptions? shrinkage, ConfidenceOptions? confidence = null)
    {
        _store.StoreAsync(Arg.Do<CellSnapshotToStore>(s => _written = s), Ct).Returns(true);
        return new ComputeCellStatsHandler(_runs, _store, _figures, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance, confidence, shrinkage);
    }

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    // 40 buys (one cell) and 12 sells (another) of one strategy: the sell cell has a parent with 40 trades, the buy cell has one with 12 only
    private void Run()
    {
        _runs.FindAsync(KeyA, Ct).Returns(new StoredRunHeader(1, KeyA, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t"));
        var buys = Enumerable.Range(0, 40).Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, AsOf.AddDays(-30 + (i / 2)), rFilled: (i % 2 == 0 ? 1.0 : -0.6), rExact: 1));
        var sells = Enumerable.Range(0, 12).Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Sell, AsOf.AddDays(-30 + (i / 2)), rFilled: (i % 2 == 0 ? 1.4 : -0.2), rExact: 1));
        _runs.StreamTradesAsync(KeyA, false, Ct).Returns(Stream([.. buys, .. sells]));
    }

    private static ComputeCellStatsCommand Command() => new(AsOf, [new CellSource(KeyA, "intraday", "FIXED_RR")], new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 5, MinTrades: 2), "test");

    [Fact]
    public async Task Every_row_carries_the_shrinkage_and_the_sell_cell_is_pulled_toward_the_buy_cell()
    {
        Run();

        var result = await Handler(new ShrinkageOptions()).HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        _written!.Rows.ShouldAllBe(r => r.Shrinkage != null && r.Shrinkage.Mode == ConfidenceMode.ReportOnly);
        var sell = _written.Rows.Single(r => r.Cell.Side == "SELL" && r.Window == CellWindow.All);
        sell.Shrinkage!.IsAvailable.ShouldBeTrue();
        sell.Shrinkage.ParentLevel.ShouldBe("strategy");
        var buy = _written.Rows.Single(r => r.Cell.Side == "BUY" && r.Window == CellWindow.All);
        buy.Shrinkage!.IsAvailable.ShouldBeFalse();   // its only sibling has 12 trades
        buy.Shrinkage.Note.ShouldBe("unavailable: no parent with 30 trades");
        result.Value.ShrunkRows.ShouldBe(_written.Rows.Count(r => r.Shrinkage!.IsAvailable));
        result.Value.TauFloorRows.ShouldBe(result.Value.ShrunkRows);   // two cells: no estimate, the floor
        result.Value.TauFloorShare.ShouldBe(1.0);
    }

    [Fact]
    public async Task Without_the_options_or_with_Off_the_rows_and_the_key_are_what_they_always_were()
    {
        Run();
        var none = await Handler(null).HandleAsync(Command(), Ct);
        none.Value.ShrunkRows.ShouldBe(0);
        none.Value.TauFloorShare.ShouldBeNull();
        _written!.Rows.ShouldAllBe(r => r.Shrinkage == null);
        Run();
        var off = await Handler(new ShrinkageOptions(Mode: ConfidenceMode.Off)).HandleAsync(Command(), Ct);

        none.Value.SnapshotKey.ShouldBe(ComputeCellStatsHandler.KeyOf(AsOf, Command().Options, $"{KeyA}:intraday:FIXED_RR"));
        off.Value.SnapshotKey.ShouldBe(none.Value.SnapshotKey);
    }

    [Fact]
    public async Task A_mode_or_setting_change_is_a_new_snapshot_and_the_confidence_key_part_is_kept()
    {
        Run();
        var none = (await Handler(null).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var report = (await Handler(new ShrinkageOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var again = (await Handler(new ShrinkageOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var floor = (await Handler(new ShrinkageOptions(TauSquaredFloor: 0.02)).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var withConfidence = (await Handler(new ShrinkageOptions(), new ConfidenceOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var confidenceOnly = (await Handler(null, new ConfidenceOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;

        report.ShouldNotBe(none);
        again.ShouldBe(report);
        floor.ShouldNotBe(report);
        confidenceOnly.ShouldBe(ComputeCellStatsHandler.KeyOf(AsOf, Command().Options, $"{KeyA}:intraday:FIXED_RR", null, new ConfidenceOptions()));
        withConfidence.ShouldNotBe(confidenceOnly);
    }

    [Fact]
    public async Task The_sigma_floor_is_part_of_the_shrinkage_key_even_when_effective_n_is_off()
    {
        var off = new ConfidenceOptions(Mode: ConfidenceMode.Off);
        Run();
        var a = (await Handler(new ShrinkageOptions(), off).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var b = (await Handler(new ShrinkageOptions(), off with { SigmaFloorR = 0.6 }).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var aAgain = (await Handler(new ShrinkageOptions(), off).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var shrinkOff = (await Handler(new ShrinkageOptions(Mode: ConfidenceMode.Off), off with { SigmaFloorR = 0.6 }).HandleAsync(Command(), Ct)).Value.SnapshotKey;

        a.ShouldNotBe(b);
        aAgain.ShouldBe(a);
        shrinkOff.ShouldBe(ComputeCellStatsHandler.KeyOf(AsOf, Command().Options, $"{KeyA}:intraday:FIXED_RR"));
    }

    [Fact]
    public async Task The_raw_fields_are_the_same_with_and_without_the_shrinkage()
    {
        Run();
        await Handler(null).HandleAsync(Command(), Ct);
        var plain = _written!.Rows.ToList();
        Run();
        await Handler(new ShrinkageOptions()).HandleAsync(Command(), Ct);

        _written!.Rows.Count.ShouldBe(plain.Count);
        for (var i = 0; i < plain.Count; i++)
        {
            (_written.Rows[i] with { Shrinkage = null }).ShouldBe(plain[i]);
        }
    }
}

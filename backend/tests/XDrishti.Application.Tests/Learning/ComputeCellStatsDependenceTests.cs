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

/// <summary>CF-01: the compute handler reads the entry date of each stored trade as its cluster, writes the dependence block on every row, and keeps the key of a snapshot without it.</summary>
public sealed class ComputeCellStatsDependenceTests
{
    private static readonly string KeyA = new('a', 64);

    private static readonly DateOnly AsOf = new(2026, 3, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ICellStatsStore _store = Substitute.For<ICellStatsStore>();

    private readonly IContextFigures _figures = Substitute.For<IContextFigures>();

    private CellSnapshotToStore? _written;

    private ComputeCellStatsHandler Handler(ConfidenceOptions? confidence)
    {
        _store.StoreAsync(Arg.Do<CellSnapshotToStore>(s => _written = s), Ct).Returns(true);
        return new ComputeCellStatsHandler(_runs, _store, _figures, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero)), NullLogger<ComputeCellStatsHandler>.Instance, confidence);
    }

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    // 12 trades, 2 on each of 6 entry sessions; the sessions are far apart in R so the cluster estimate is well defined
    private void Run()
    {
        _runs.FindAsync(KeyA, Ct).Returns(new StoredRunHeader(1, KeyA, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t"));
        var trades = Enumerable.Range(0, 12).Select(i => FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, AsOf.AddDays(-20 + (i / 2)), rFilled: ((i / 2 % 2 == 0) ? 1.0 : -1.0) + (i % 2 * 0.2), rExact: 1)).ToArray();
        _runs.StreamTradesAsync(KeyA, false, Ct).Returns(Stream(trades));
    }

    private static ComputeCellStatsCommand Command() => new(AsOf, [new CellSource(KeyA, "intraday", "FIXED_RR")], new CellStatsOptions(RecencyHalfLifeDays: 30, PriorStrength: 0, MinTrades: 2), "test");

    [Fact]
    public async Task Every_row_carries_the_block_with_the_entry_session_as_cluster()
    {
        Run();

        var result = await Handler(new ConfidenceOptions()).HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        var all = _written!.Rows.Single(r => r.Window == CellWindow.All);
        all.Dependence.ShouldNotBeNull();
        all.Dependence!.N.ShouldBe(12);
        all.Dependence.Method.ShouldBe("cluster");
        all.Dependence.NEff!.Value.ShouldBeLessThan(12);
        all.Dependence.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        _written.Rows.ShouldAllBe(r => r.Dependence != null);
    }

    [Fact]
    public async Task Without_the_options_the_rows_and_the_key_are_what_they_always_were()
    {
        Run();

        var result = await Handler(null).HandleAsync(Command(), Ct);

        _written!.Rows.ShouldAllBe(r => r.Dependence == null);
        result.Value.SnapshotKey.ShouldBe(ComputeCellStatsHandler.KeyOf(AsOf, Command().Options, $"{KeyA}:intraday:FIXED_RR"));
    }

    [Fact]
    public async Task Off_is_the_same_as_none_and_a_mode_or_setting_change_is_a_new_snapshot_key()
    {
        Run();
        var none = (await Handler(null).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var off = (await Handler(new ConfidenceOptions(Mode: ConfidenceMode.Off)).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var report = (await Handler(new ConfidenceOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var reportAgain = (await Handler(new ConfidenceOptions()).HandleAsync(Command(), Ct)).Value.SnapshotKey;
        Run();
        var gate = (await Handler(new ConfidenceOptions(Mode: ConfidenceMode.Gate)).HandleAsync(Command(), Ct)).Value.SnapshotKey;

        off.ShouldBe(none);
        report.ShouldNotBe(none);
        reportAgain.ShouldBe(report);
        gate.ShouldNotBe(report);
    }
}

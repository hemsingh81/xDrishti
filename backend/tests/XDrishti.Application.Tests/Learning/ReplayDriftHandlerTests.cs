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

/// <summary>The drift replay use case (spec P6-09): stored run to drift events, the refusals, and that nothing is written.</summary>
public sealed class ReplayDriftHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private ReplayDriftHandler Handler() => new(_runs, Substitute.For<ITradeFeatureStore>(), _registry, Substitute.For<IModelFiles>(), Substitute.For<IModelScorerLoader>(), new FakeTimeProvider(), NullLogger<ReplayDriftHandler>.Instance);

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static ReplayDriftQuery Query(string runKey = "", int months = 1, long? model = null) =>
        new([new CellSource(runKey.Length > 0 ? runKey : Key, "intraday", "FIXED_RR")], new DriftOptions(), months, model, new CellStatsOptions(), null);

    /// <summary>One trade a day for 90 days; 60 reference days of +-1 around 0.3 R, then 30 days of -0.5 R.</summary>
    private static SimTrade[] Trades() =>
        [.. Enumerable.Range(0, 90).Select(i =>
        {
            var r = i < 60 ? (i % 2 == 0 ? 1.3 : -0.7) : -0.5;
            return FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(2026, 1, 1).AddDays(i), ExitReason.Stop, rExact: r, rFilled: r);
        })];

    [Fact]
    public async Task A_decaying_cell_in_the_last_month_raises_a_cusum_alarm_and_a_proposal()
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(Trades()));

        var result = await Handler().HandleAsync(Query(), Ct);

        result.IsSuccess.ShouldBeTrue();
        var cell = result.Value.Cells.ShouldHaveSingleItem();
        cell.CellKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        cell.Band.ShouldNotBeNull();
        cell.Events.ShouldContain(e => e.Detector == DriftDetectorKind.Cusum && e.Fired);
        result.Value.Proposals.ShouldHaveSingleItem().Pause.ShouldBeTrue();
        result.Value.Calibration.ShouldBeNull();
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var bad = await Handler().HandleAsync(Query("nope"), Ct);
        var months = await Handler().HandleAsync(Query(months: 0), Ct);

        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        months.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task An_unknown_model_is_not_found_and_nothing_is_designated()
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(Trades()));
        _registry.FindAsync(9, Ct).Returns((StoredModel?)null);

        var result = await Handler().HandleAsync(Query(model: 9), Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
        await _registry.DidNotReceiveWithAnyArgs().DesignateAsync(default, default, default!, default!, default, Ct);
    }
}

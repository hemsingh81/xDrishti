using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P6-10: triggers are evaluated on stored runs only and come back as proposals; nothing is written.</summary>
public sealed class EvaluateHypothesisTriggersHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IHypothesisLog _log = Substitute.For<IHypothesisLog>();

    private readonly IQueryHandler<ReplayDriftQuery, Result<DriftReport>> _drift = Substitute.For<IQueryHandler<ReplayDriftQuery, Result<DriftReport>>>();

    private static async IAsyncEnumerable<RunTrade> Stream(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new RunTrade(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(2026, 3, 2).AddDays(i % 20), rExact: 0.5, rFilled: 0.5), []);
        }
    }

    private EvaluateHypothesisTriggersHandler Handler()
    {
        _runs.FindAsync(Key, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, Key, "sim", 5, 5, "ORB", new string('c', 64), new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "t"));
        _runs.StreamTradesAsync(Key, false, Arg.Any<CancellationToken>()).Returns(_ => Stream(35));
        _log.CountOpenAsync(Arg.Any<CancellationToken>()).Returns(2);
        return new EvaluateHypothesisTriggersHandler(_runs, _log, _drift, NullLogger<EvaluateHypothesisTriggersHandler>.Instance);
    }

    [Fact]
    public async Task New_trades_and_a_drift_alarm_are_returned_as_proposals_and_nothing_is_written()
    {
        var handler = Handler();
        _drift.HandleAsync(Arg.Any<ReplayDriftQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new DriftReport(new DriftOptions(), 0, [], null, [new DriftProposal("ORB|15m|BUY|intraday|FIXED_RR|*", 5, true, "below the band")], [])));

        var result = await handler.HandleAsync(new EvaluateHypothesisTriggersQuery([new CellSource(Key, "intraday", "FIXED_RR")], new DateOnly(2026, 1, 1), 30, new DriftOptions(), 6), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Triggers.Select(t => t.Kind).ShouldBe(["new_trades", "drift"]);
        result.Value.OpenHypotheses.ShouldBe(2);
        result.Value.Notes[0].ShouldContain("nothing was written");
        await _log.DidNotReceiveWithAnyArgs().ProposeAsync(default!, 0, Ct);
        await _log.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
    }

    [Fact]
    public async Task Below_the_threshold_there_is_no_new_trades_trigger()
    {
        var handler = Handler();
        _drift.HandleAsync(Arg.Any<ReplayDriftQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new DriftReport(new DriftOptions(), 0, [], null, [], [])));

        var result = await handler.HandleAsync(new EvaluateHypothesisTriggersQuery([new CellSource(Key, "intraday", "FIXED_RR")], new DateOnly(2026, 1, 1), 36, new DriftOptions(), 6), Ct);

        result.Value.Triggers.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_request_is_refused_and_an_unknown_run_is_not_found()
    {
        var handler = Handler();
        _runs.FindAsync(Key, Arg.Any<CancellationToken>()).Returns((StoredRunHeader?)null);

        var bad = await handler.HandleAsync(new EvaluateHypothesisTriggersQuery([], new DateOnly(2026, 1, 1), 0, new DriftOptions(), 0), Ct);
        var missing = await handler.HandleAsync(new EvaluateHypothesisTriggersQuery([new CellSource(Key, "intraday", "X")], new DateOnly(2026, 1, 1), 30, new DriftOptions(), 6), Ct);

        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        missing.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }
}

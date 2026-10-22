using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>The stages of a Lab run against fake ports (P5-02 criteria 1, 4, 6, 7): validate, compile and quick test in order; a failed compile writes nothing; a cancel stores nothing; the host's stop leaves the run running.</summary>
public sealed class LabRunnerTests
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero));

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IStrategyChecker _checker = Substitute.For<IStrategyChecker>();

    private IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly ICommandHandler<StoreRunCommand, StoreOutcome> _store = Substitute.For<ICommandHandler<StoreRunCommand, StoreOutcome>>();

    private readonly IRunRecordWriter _writer = Substitute.For<IRunRecordWriter>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private readonly ISimulationEngine _engine = Substitute.For<ISimulationEngine>();

    private readonly ILabProgress _progress = Substitute.For<ILabProgress>();

    private readonly SimulationRun _run = FabricatedRun.Standard();

    private readonly List<LabFinish> _finishes = [];

    private static LabRun Run() => new(
        Guid.NewGuid(), new string('k', 64), "LAB_TEST", 1, Hash,
        new LabUniverse("symbols", null, "symbols as listed", 2, [new LabInstrument(FabricatedRun.IdA, "AAA"), new LabInstrument(FabricatedRun.IdB, "BBB")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", new string('c', 64), "sim-5", LabRunState.Running, "validate", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static StrategyIssue Issue(string severity, string code = "strategy.lint.unknown_name") => new("buy.trigger[1]", code, "message", severity, 3, 5, 3, 9, 0, 4, null);

    public LabRunnerTests()
    {
        _strategies.GetByHashAsync(Hash, Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 1, 1, Hash, "the text", "{}", null, null, DateTimeOffset.UnixEpoch));
        _checker.Check("the text").Returns(new StrategyCheck(new StrategyDescription("LAB_TEST", "intraday", Hash, "{}", [Timeframe.M15], [Side.Buy], []), 1, [Issue("warning", "strategy.lint.note")]));
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(_run));
        _writer.Write(_run).Returns(new RunRecordText("{}", new string('1', 64), new string('2', 64)));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(StoreOutcome.Stored(1, 0)));
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<CostSheet>(Error.NotFound("costs.sheet_not_found", "none")));
        _engine.BuiltIns.Returns([]);
        _runs.FinishAsync(Arg.Any<LabFinish>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _finishes.Add(ci.Arg<LabFinish>());
            return true;
        });
    }

    private LabRunner Runner() => new(_runs, _strategies, _checker, _simulation, _store, _writer, _sheets, _engine, _progress, LabSettings.Default, _time, NullLogger<LabRunner>.Instance);

    [Fact]
    public async Task A_valid_run_goes_through_the_three_stages_stores_the_simulation_once_and_succeeds_with_its_key_and_counts()
    {
        var run = Run();

        await Runner().ExecuteAsync(run, Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Succeeded);
        finish.SimRunKeys.ShouldBe([_run.RunKey]);
        finish.Counts!.Trades.ShouldBe(_run.Trades.Count);
        finish.Counts.OpenAtEnd.ShouldBe(_run.OpenAtEnd.Count);
        finish.Stages.Select(s => s.Name).ShouldBe(["validate", "compile", "quick_test"]);
        finish.Stages.ShouldAllBe(s => s.FinishedAt != null);
        finish.Problems.Select(p => p.Code).ShouldBe(["strategy.lint.note"], "the warning is kept, it does not fail the run");
        await _store.Received(1).HandleAsync(Arg.Is<StoreRunCommand>(c => c.Run == _run && c.Actor == LabRunner.Actor), Arg.Any<CancellationToken>());
        await _progress.Received(1).FinishedAsync(Arg.Is<LabRunFinished>(f => f.RunId == run.Id && f.State == "succeeded"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_simulation_is_asked_for_the_stored_version_by_hash_over_the_resolved_instruments_with_the_modelled_fills_and_gross_when_no_sheet_is_stored()
    {
        await Runner().ExecuteAsync(Run(), Ct);

        var query = (RunSimulationQuery)_simulation.ReceivedCalls().Single().GetArguments()[0]!;
        query.Request.StrategyHash.ShouldBe(Hash);
        query.Request.StrategyVersion.ShouldBeNull("a hash names the version; the request gives the hash only");
        query.Request.InstrumentIds.ShouldBe([FabricatedRun.IdA, FabricatedRun.IdB]);
        query.Request.BasketId.ShouldBeNull();
        query.Request.EvaluationTimeframe.ShouldBe("15m");
        query.Request.From.ShouldBe(new DateOnly(2025, 5, 1));
        query.Request.To.ShouldBe(new DateOnly(2026, 4, 30));
        query.Request.Costs.ShouldBe(CostChoice.Gross);
        query.Request.Fills.ShouldBe(new FillRulesInput(FillRules.Default.EntryLimitPercent, FillRules.Default.Slippage.Ticks, FillRules.Default.Slippage.BarRangeFrac, FillRules.Default.Slippage.StopExitExtraTicks));
    }

    [Fact]
    public async Task A_text_that_fails_to_compile_fails_the_run_at_compile_with_its_located_problems_and_runs_and_stores_nothing()
    {
        _checker.Check("the text").Returns(new StrategyCheck(null, null, [Issue("error")]));

        await Runner().ExecuteAsync(Run(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Failed);
        finish.Stage.ShouldBe("compile");
        finish.Error.ShouldBe("lab.compile_failed");
        finish.Problems.Single().ShouldBe(new LabProblem("compile", "buy.trigger[1]", "strategy.lint.unknown_name", "message", "error", 3, 5));
        finish.Stages.Select(s => s.Name).ShouldBe(["validate", "compile"]);
        await _simulation.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_version_that_is_no_longer_stored_fails_the_run_at_validate()
    {
        _strategies.GetByHashAsync(Hash, Arg.Any<CancellationToken>()).Returns((StoredVersion?)null);

        await Runner().ExecuteAsync(Run(), Ct);

        _finishes.Single().Stage.ShouldBe("validate");
        _finishes.Single().Problems.Single().Code.ShouldBe("lab.strategy_missing");
        _checker.DidNotReceiveWithAnyArgs().Check(default!);
    }

    [Fact]
    public async Task A_refusal_of_the_simulation_fails_the_run_at_the_quick_test_with_the_fields_as_problems_and_stores_nothing()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SimulationRun>(SimulationErrors.StrategyInvalid([new FieldError("buy.stop", "no stop", null, "simulation.x")])));

        await Runner().ExecuteAsync(Run(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Failed);
        finish.Stage.ShouldBe("quick_test");
        finish.Problems.Single(p => p.Stage == "quick_test").Code.ShouldBe("simulation.x");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_cancel_asked_for_before_the_store_is_written_ends_the_run_cancelled_and_stores_nothing()
    {
        _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        await Runner().ExecuteAsync(Run(), Ct);

        _finishes.Single().State.ShouldBe(LabRunState.Cancelled);
        _finishes.Single().SimRunKeys.ShouldBeEmpty();
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _progress.Received(1).FinishedAsync(Arg.Is<LabRunFinished>(f => f.State == "cancelled"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cancel_during_the_replay_is_seen_by_the_watcher_stops_the_replay_and_stores_nothing()
    {
        _simulation = new Scripted(token => WaitForCancelAsync(token));
        _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var running = Runner().ExecuteAsync(Run(), Ct);
        await AdvanceUntilAsync(running);

        _finishes.Single().State.ShouldBe(LabRunState.Cancelled);
        _finishes.Single().Stage.ShouldBe("quick_test");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_stop_of_the_host_leaves_the_run_running_and_writes_no_end()
    {
        using var stop = new CancellationTokenSource();
        _simulation = new Scripted(token => StopTheHostAsync(stop, token));

        await Runner().ExecuteAsync(Run(), stop.Token);

        _finishes.ShouldBeEmpty();
        await _progress.DidNotReceiveWithAnyArgs().FinishedAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unexpected_exception_fails_the_run_with_a_generic_code_and_not_its_message()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("secret detail"));

        await Runner().ExecuteAsync(Run(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Failed);
        finish.Error.ShouldBe("lab.run_crashed");
        finish.Problems.ShouldBeEmpty();
    }

    private async ValueTask<Result<SimulationRun>> WaitForCancelAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Result.Success(_run);
    }

    private async ValueTask<Result<SimulationRun>> StopTheHostAsync(CancellationTokenSource stop, CancellationToken token)
    {
        await stop.CancelAsync();
        token.ThrowIfCancellationRequested();
        return Result.Success(_run);
    }

    private async Task AdvanceUntilAsync(Task running)
    {
        for (var i = 0; i < 2000 && !running.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Yield();
        }

        await running.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    /// <summary>A simulation handler whose answer is a function of the token it was given.</summary>
    private sealed class Scripted(Func<CancellationToken, ValueTask<Result<SimulationRun>>> answer) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>
    {
        public ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken) => answer(cancellationToken);
    }
}

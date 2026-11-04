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
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
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

    private readonly IQueryHandler<RunComparisonQuery, Result<ComparisonRun>> _comparisons = Substitute.For<IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>>();

    private readonly XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar _calendars = Substitute.For<XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar>();

    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();

    private readonly IVariantStatsStore _variantStats = Substitute.For<IVariantStatsStore>();

    private readonly ILabResultStore _results = Substitute.For<ILabResultStore>();

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
        _ledger.CountTrialsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new XDrishti.Domain.Lab.TrialCounts(0, 0, 0, 0, 0));
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

    private LabRunner RunnerWithDeflation(DeflationOptions? options = null)
    {
        _ledger.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _variantStats.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _variantStats.AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IReadOnlyList<VariantStatsRow>>().Count);
        return new(_runs, _strategies, _checker, _simulation, _store, _writer, _sheets, _engine, new LabFullTest(_comparisons, _calendars, _ledger, LabSettings.Default, new ConfidenceOptions()), new LabRobustness(_simulation, LabSettings.Default), _ledger, _results, _progress, LabSettings.Default, _time, NullLogger<LabRunner>.Instance,
            new LabDeflation(_ledger, _variantStats, options ?? new DeflationOptions()), _variantStats);
    }

    private LabRunner RunnerWithStress(CostStressOptions options)
    {
        _ledger.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _variantStats.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _variantStats.AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IReadOnlyList<VariantStatsRow>>().Count);
        return new(_runs, _strategies, _checker, _simulation, _store, _writer, _sheets, _engine, new LabFullTest(_comparisons, _calendars, _ledger, LabSettings.Default, new ConfidenceOptions()), new LabRobustness(_simulation, LabSettings.Default, null, null, options), _ledger, _results, _progress, LabSettings.Default, _time, NullLogger<LabRunner>.Instance,
            new LabDeflation(_ledger, _variantStats, new DeflationOptions(ConfidenceMode.Off)), _variantStats, new LabCostStress(options));
    }

    private LabRunner Runner(ConfidenceOptions? confidence = null) => new(_runs, _strategies, _checker, _simulation, _store, _writer, _sheets, _engine, new LabFullTest(_comparisons, _calendars, _ledger, LabSettings.Default, confidence), new LabRobustness(_simulation, LabSettings.Default), _ledger, _results, _progress, LabSettings.Default, _time, NullLogger<LabRunner>.Instance, new LabDeflation(_ledger, _variantStats, new DeflationOptions(ConfidenceMode.Off)), _variantStats);

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

    private static LabRun FullRun() => Run() with { Method = "full", From = new DateOnly(2026, 2, 1) };

    [Fact]
    public async Task A_full_test_without_a_stored_sheet_fails_the_run_at_the_full_test_stage_and_runs_nothing()
    {
        await Runner().ExecuteAsync(FullRun(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Failed);
        finish.Stage.ShouldBe("full_test");
        finish.Error.ShouldBe("lab.costs_required");
        await _comparisons.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _results.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_refusal_of_the_comparison_fails_the_full_test_stores_no_run_and_no_result()
    {
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _comparisons.HandleAsync(Arg.Any<RunComparisonQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ComparisonRun>(SimulationErrors.StrategyInvalid([new FieldError("buy.stop", "no stop", null, "simulation.x")])));
        _calendars.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());

        await Runner().ExecuteAsync(FullRun(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Failed);
        finish.Stage.ShouldBe("full_test");
        finish.Problems.Single(p => p.Stage == "full_test").Code.ShouldBe("simulation.x");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _results.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_full_test_asks_the_comparison_for_the_candidate_exits_costed_over_the_whole_window()
    {
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _comparisons.HandleAsync(Arg.Any<RunComparisonQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ComparisonRun>(Error.Validation("x", "stop here")));
        _calendars.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());

        await Runner().ExecuteAsync(FullRun(), Ct);

        var query = (RunComparisonQuery)_comparisons.ReceivedCalls().Single().GetArguments()[0]!;
        query.Request.Exits!.Candidates.Select(c => c.Id).ShouldBe(["FIXED_RR{rr=2}", "FIXED_RR{rr=1.5}", "FIXED_RR{rr=2.5}", "FIXED_RR{rr=3}"]);
        query.Request.Costs.ShouldBeOfType<CostChoice.SheetChoice>();
        query.Request.From.ShouldBe(new DateOnly(2026, 2, 1));
        query.Request.StrategyHash.ShouldBe(Hash);
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

    private static LabRun RobustRun() => FullRun() with { Method = "robust" };

    /// <summary>A robust or full run whose comparison succeeds with the four candidate exits and whose strategy declares one parameter (so the sweep has points).</summary>
    private void ArrangeFullTest()
    {
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _calendars.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        _checker.Check("the text").Returns(new StrategyCheck(new StrategyDescription("LAB_TEST", "intraday", Hash, "{}", [Timeframe.M15], [Side.Buy], [new KeyValuePair<string, double>("period", 10)]), 1, []));
        var candidates = ExitFlags.Parse([.. LabSettings.Default.Exits], null, [])!.Candidates
            .Select((exit, i) => new CandidateRun(exit.Id, exit, FabricatedRun.CostedStandard() with { RunKey = new string((char)('1' + i), 64) }, 0))
            .ToList();
        _comparisons.HandleAsync(Arg.Any<RunComparisonQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ComparisonRun(candidates, ComparisonReport.Build(candidates, true), new string('e', 64))));
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LabVerdictView>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task A_robust_run_has_the_same_verdict_as_a_full_run_on_the_same_input_plus_the_block()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await Runner().ExecuteAsync(FullRun(), Ct);
        await Runner().ExecuteAsync(RobustRun(), Ct);

        verdicts.Count.ShouldBe(2);
        verdicts[0].Robustness.ShouldBeNull();
        verdicts[1].Robustness.ShouldNotBeNull();
        System.Text.Json.JsonSerializer.Serialize(verdicts[1] with { Robustness = null }).ShouldBe(System.Text.Json.JsonSerializer.Serialize(verdicts[0]));
    }

    [Fact]
    public async Task Effective_n_in_report_only_adds_the_block_and_leaves_the_outcome_the_gates_and_the_evidence_as_they_were()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await Runner().ExecuteAsync(FullRun(), Ct);
        await Runner(new ConfidenceOptions()).ExecuteAsync(FullRun(), Ct);

        verdicts.Count.ShouldBe(2);
        verdicts[0].Evidence.Dependence.ShouldBeNull();
        verdicts[1].Outcome.ShouldBe(verdicts[0].Outcome);
        verdicts[1].Gates.ShouldBe(verdicts[0].Gates);
        (verdicts[1].Evidence with { Dependence = null }).ShouldBe(verdicts[0].Evidence);
        verdicts[1].Evidence.Dependence.ShouldNotBeNull();
        verdicts[1].Evidence.Dependence!.Mode.ShouldBe("ReportOnly");
        verdicts[1].Evidence.Dependence!.Settings.ShouldBe(new ConfidenceOptions().KeyText());
        verdicts[1].Limits.ShouldContain(XDrishti.Application.Confidence.DependenceView.LimitsText);
        verdicts[0].Limits.ShouldNotContain(XDrishti.Application.Confidence.DependenceView.LimitsText);
    }

    [Fact]
    public async Task A_robust_run_goes_through_validate_compile_full_test_and_robustness_in_that_order()
    {
        ArrangeFullTest();

        await Runner().ExecuteAsync(RobustRun(), Ct);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Succeeded);
        finish.Stages.Select(s => s.Name).ShouldBe(["validate", "compile", "full_test", "robustness"]);
    }

    [Fact]
    public async Task A_cancel_during_the_robustness_stage_stores_no_result_no_run_and_no_sweep_ledger_row()
    {
        ArrangeFullTest();
        _simulation = new Scripted(token => WaitForCancelAsync(token));
        _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var running = Runner().ExecuteAsync(RobustRun(), Ct);
        await AdvanceUntilAsync(running);

        var finish = _finishes.Single();
        finish.State.ShouldBe(LabRunState.Cancelled);
        finish.Stage.ShouldBe("robustness");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
        await _results.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_cancel_seen_after_the_sweep_ran_writes_no_ledger_row()
    {
        ArrangeFullTest();
        _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        await Runner().ExecuteAsync(RobustRun(), Ct);

        _finishes.Single().State.ShouldBe(LabRunState.Cancelled);
        await _simulation.ReceivedWithAnyArgs().HandleAsync(default!, Ct);
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _results.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public async Task The_sweep_ledger_rows_are_appended_after_the_runs_are_stored_and_before_the_result()
    {
        ArrangeFullTest();
        var calls = new List<string>();
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("run");
            return Result.Success(StoreOutcome.Stored(1, 0));
        });
        _ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("ledger");
            return true;
        });
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LabVerdictView>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("result");
            return true;
        });

        await Runner().ExecuteAsync(RobustRun(), Ct);

        calls.Count(c => c == "ledger").ShouldBe(4);
        calls.Last().ShouldBe("result");
        calls.LastIndexOf("run").ShouldBeLessThan(calls.IndexOf("ledger"));
        calls.LastIndexOf("ledger").ShouldBeLessThan(calls.IndexOf("result"));
        _finishes.Single().State.ShouldBe(LabRunState.Succeeded);
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

    [Fact]
    public async Task The_cost_stress_in_report_only_adds_the_block_and_leaves_the_verdict_the_sim_run_keys_and_the_stored_runs_byte_for_byte_as_they_were()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithStress(new CostStressOptions(ConfidenceMode.Off)).ExecuteAsync(RobustRun(), Ct);
        await RunnerWithStress(new CostStressOptions()).ExecuteAsync(RobustRun(), Ct);

        verdicts.Count.ShouldBe(2);
        verdicts[0].CostStress.ShouldBeNull();
        var block = verdicts[1].CostStress.ShouldNotBeNull();
        block.Mode.ShouldBe("ReportOnly");
        block.Enforce.ShouldBeFalse();
        block.CostsChecked.ShouldBeFalse("the fixture sheet carries no bill check");
        block.SheetId.ShouldBe("fixture-a");
        block.SheetRatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        block.Rows.ShouldAllBe(r => r.Status == "unavailable");
        block.Delay.ShouldNotBeNull();
        System.Text.Json.JsonSerializer.Serialize(verdicts[1] with { CostStress = null }).ShouldBe(System.Text.Json.JsonSerializer.Serialize(verdicts[0]), "outcome, gates, reasons, limits, evidence and robustness are byte for byte the same");
        _finishes[1].SimRunKeys.ShouldBe(_finishes[0].SimRunKeys);
    }

    [Fact]
    public async Task A_bill_check_on_the_sheet_removes_the_label_and_the_rows_are_judged()
    {
        ArrangeFullTest();
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Build("fixture-a", "Dhan contract notes, 2026-11-03")));
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithStress(new CostStressOptions()).ExecuteAsync(RobustRun(), Ct);

        var block = verdicts.Single().CostStress.ShouldNotBeNull();
        block.CostsChecked.ShouldBeTrue();
        block.Rows.ShouldNotContain(r => r.Reason.Contains("not checked against a bill", StringComparison.Ordinal));
        block.Rows.ShouldAllBe(r => r.Status != "unavailable" || r.ExpectancyR == null, "a row is unavailable only for want of a figure");
    }

    [Fact]
    public async Task Enforce_on_never_raises_the_outcome_of_the_run()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithStress(new CostStressOptions(ConfidenceMode.Off)).ExecuteAsync(RobustRun(), Ct);
        await RunnerWithStress(new CostStressOptions(Enforce: true)).ExecuteAsync(RobustRun(), Ct);

        var rank = new Dictionary<string, int> { ["FAIL"] = 0, ["MARGINAL"] = 1, ["PASS"] = 2 };
        rank[verdicts[1].Outcome].ShouldBeLessThanOrEqualTo(rank[verdicts[0].Outcome]);
        verdicts[1].Gates.ShouldBe(verdicts[0].Gates);
    }

    [Fact]
    public async Task The_delay_pass_asks_for_the_same_request_with_an_entry_delay_of_one_minute_and_a_full_test_takes_none()
    {
        ArrangeFullTest();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(_run));

        await RunnerWithStress(new CostStressOptions()).ExecuteAsync(RobustRun(), Ct);

        var requests = _simulation.ReceivedCalls().Select(c => ((RunSimulationQuery)c.GetArguments()[0]!).Request).ToList();
        requests.Count.ShouldBe(6, "the base, the four sweep points and the delayed run");
        requests.Count(r => r.Fills!.EntryDelayMinutes == 1).ShouldBe(1);
        requests.Last().Fills!.EntryDelayMinutes.ShouldBe(1);
        requests.Last().Parameters.ShouldBeEmpty();
        requests.Take(5).ShouldAllBe(r => r.Fills!.EntryDelayMinutes == 0);

        _simulation.ClearReceivedCalls();
        await RunnerWithStress(new CostStressOptions()).ExecuteAsync(FullRun(), Ct);
        _simulation.ReceivedCalls().ShouldBeEmpty("a full test has no simulation pass of its own");
    }

    [Fact]
    public async Task A_plain_full_test_has_no_cost_stress_member_so_its_document_is_what_it_was()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithStress(new CostStressOptions()).ExecuteAsync(FullRun(), Ct);
        await RunnerWithStress(new CostStressOptions(ConfidenceMode.Off)).ExecuteAsync(FullRun(), Ct);

        verdicts[0].CostStress.ShouldBeNull();
        System.Text.Json.JsonSerializer.Serialize(verdicts[0]).ShouldBe(System.Text.Json.JsonSerializer.Serialize(verdicts[1]));
    }

    /// <summary>A simulation handler whose answer is a function of the token it was given.</summary>
    private sealed class Scripted(Func<CancellationToken, ValueTask<Result<SimulationRun>>> answer) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>
    {
        public ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken) => answer(cancellationToken);
    }

    [Fact]
    public async Task A_robust_run_stores_the_statistics_of_the_judged_variant_and_each_sweep_point_after_the_ledger_rows_and_before_the_result()
    {
        ArrangeFullTest();
        var calls = new List<string>();
        var stored = new List<VariantStatsRow>();
        _ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("ledger");
            return true;
        });
        var runner = RunnerWithDeflation();
        _variantStats.AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            calls.Add("stats");
            stored.AddRange(ci.Arg<IReadOnlyList<VariantStatsRow>>());
            return 5;
        });
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LabVerdictView>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("result");
            return true;
        });
        var run = RobustRun();

        await runner.ExecuteAsync(run, Ct);

        calls.Last().ShouldBe("result");
        calls.ShouldContain("stats");
        calls.IndexOf("stats").ShouldBeGreaterThan(calls.LastIndexOf("ledger"));
        calls.IndexOf("stats").ShouldBeLessThan(calls.IndexOf("result"));
        stored.Count.ShouldBe(5, "the judged variant and the four sweep points");
        stored[0].RunId.ShouldBe(run.Id);
        stored[0].VariantHash.ShouldBe(run.StrategyHash);
        stored[0].Method.ShouldBe("robust");
        stored.Skip(1).ShouldAllBe(r => r.Method == "sweep" && r.VariantHash != run.StrategyHash);
        stored.ShouldAllBe(r => r.StrategyId == "LAB_TEST");
    }

    [Fact]
    public async Task The_deflated_sharpe_ratio_changes_no_outcome_gate_evidence_or_limit_of_the_verdict()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithDeflation(new DeflationOptions(ConfidenceMode.Off)).ExecuteAsync(RobustRun(), Ct);
        await RunnerWithDeflation().ExecuteAsync(RobustRun(), Ct);

        verdicts.Count.ShouldBe(2);
        verdicts[0].DeflatedSharpe.ShouldBeNull();
        verdicts[1].DeflatedSharpe.ShouldNotBeNull().Mode.ShouldBe("ReportOnly");
        System.Text.Json.JsonSerializer.Serialize(verdicts[1] with { DeflatedSharpe = null }).ShouldBe(System.Text.Json.JsonSerializer.Serialize(verdicts[0]), "outcome, six gates, reasons, limits, evidence and robustness are byte for byte the same");
    }

    [Fact]
    public async Task Off_still_stores_the_statistics_but_shows_no_figure()
    {
        ArrangeFullTest();

        await RunnerWithDeflation(new DeflationOptions(ConfidenceMode.Off)).ExecuteAsync(RobustRun(), Ct);

        await _variantStats.Received(1).AppendAsync(Arg.Is<IReadOnlyList<VariantStatsRow>>(r => r.Count == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_full_run_with_one_trial_says_unavailable_and_stores_its_own_row()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithDeflation().ExecuteAsync(FullRun(), Ct);

        var figure = verdicts.Single().DeflatedSharpe!;
        figure.Probability.ShouldBeNull();
        figure.Note.ShouldStartWith("unavailable: trial Sharpe ratios not stored");
        await _variantStats.Received(1).AppendAsync(Arg.Is<IReadOnlyList<VariantStatsRow>>(r => r.Count == 1 && r[0].Method == "full"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cancel_seen_after_the_sweep_stores_no_statistics()
    {
        ArrangeFullTest();
        _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithDeflation().ExecuteAsync(RobustRun(), Ct);

        _finishes.Single().State.ShouldBe(LabRunState.Cancelled);
        await _variantStats.DidNotReceive().AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_points_that_ran_no_trade_still_count_in_the_ledger_so_low_coverage_reads_unavailable()
    {
        ArrangeFullTest();
        // the base run and every sweep point run no trade: the points get a row with no figure, and are variants of the family
        var empty = FabricatedRun.Standard() with { Trades = [] };
        _simulation = new Scripted(_ => ValueTask.FromResult(Result.Success(empty)));
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var stored = new List<VariantStatsRow>();

        var runner = RunnerWithDeflation();
        _variantStats.AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            stored.AddRange(ci.Arg<IReadOnlyList<VariantStatsRow>>());
            return 1;
        });
        _variantStats.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([
            new VariantStatsRow("LAB_TEST", Guid.NewGuid(), new string('1', 64), 50, 0.1, 1, 0, 3, 0.10, "sweep", DateTimeOffset.UnixEpoch),
            new VariantStatsRow("LAB_TEST", Guid.NewGuid(), new string('2', 64), 50, 0.2, 1, 0, 3, 0.20, "sweep", DateTimeOffset.UnixEpoch)]);
        await runner.ExecuteAsync(RobustRun(), Ct);

        stored.Count.ShouldBe(5, "the judged variant and every sweep point, also those without a trade");
        stored.Skip(1).ShouldAllBe(r => r.N == 0 && r.SrPerTrade == null);
        var note = verdicts.Single().DeflatedSharpe!.Note;
        note.ShouldStartWith("unavailable: trial statistics stored for only");
        note.ShouldContain("of 5 variants");
    }

    // CF-06: the count of looks beside the verdict, and the honest one only when the owner switches it on

    private LabRunner RunnerWithTrials(RegistrationOptions registration, XDrishti.Domain.Lab.TrialCounts? parts, int distinct, LabSettings? settings = null, HoldOutOptions? holdOut = null, XDrishti.Application.Abstractions.Lab.IHoldOutStore? store = null, XDrishti.Application.Abstractions.Market.Bars.IBarQueries? bars = null, XDrishti.Application.Lab.HoldOutUnlocker? unlocker = null)
    {
        _ledger.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _ledger.CountVariantsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(distinct);
        _ledger.CountTrialsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(parts ?? new XDrishti.Domain.Lab.TrialCounts(distinct, 0, 0, 0, 0));
        var s = settings ?? LabSettings.Default;
        var guard = holdOut is null ? null : new XDrishti.Application.Lab.LabHoldOutGuard(store!, bars!, holdOut, _time);
        return new(_runs, _strategies, _checker, _simulation, _store, _writer, _sheets, _engine, new LabFullTest(_comparisons, _calendars, _ledger, s, new ConfidenceOptions(), registration), new LabRobustness(_simulation, s), _ledger, _results, _progress, s, _time, NullLogger<LabRunner>.Instance,
            new LabDeflation(_ledger, _variantStats, new DeflationOptions(ConfidenceMode.Off)), _variantStats, null, holdOut, guard, unlocker);
    }

    [Fact]
    public async Task The_verdict_carries_the_parts_of_the_count_and_is_judged_with_the_old_count_until_the_honest_one_is_switched_on()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var parts = new XDrishti.Domain.Lab.TrialCounts(3, 16, 9, 0, 0);

        await RunnerWithTrials(new RegistrationOptions(), parts, 19).ExecuteAsync(FullRun(), Ct);
        await RunnerWithTrials(new RegistrationOptions(CountInFactor: true), parts, 19).ExecuteAsync(FullRun(), Ct);

        verdicts[0].VariantCount.ShouldBe(19);
        verdicts[0].AdjustmentFactor.ShouldBe(2.0620, 1e-4);
        var off = verdicts[0].Trials.ShouldNotBeNull();
        (off.RunHashes, off.SweepPoints, off.ExtraExits, off.Replans, off.NullRows, off.Total).ShouldBe((3, 16, 9, 0, 0, 28));
        off.HonestFactor.ShouldBe(2.2018, 1e-4);
        off.FactorUsed.ShouldBe(2.0620, 1e-4);
        off.HonestCountApplied.ShouldBeFalse();
        verdicts[1].VariantCount.ShouldBe(28);
        verdicts[1].AdjustmentFactor.ShouldBe(2.2018, 1e-4);
        verdicts[1].Trials!.HonestCountApplied.ShouldBeTrue();
        verdicts[1].Gates.Single(g => g.Name.Contains("expectancy", StringComparison.OrdinalIgnoreCase)).Adjusted.ShouldBeGreaterThan(verdicts[0].Gates.Single(g => g.Name.Contains("expectancy", StringComparison.OrdinalIgnoreCase)).Adjusted, "the honest count tightens the gate");
    }

    [Fact]
    public async Task With_the_switch_off_everything_but_the_new_member_is_byte_for_byte_what_a_verdict_without_it_was()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithTrials(new RegistrationOptions(), null, 4).ExecuteAsync(FullRun(), Ct);
        await RunnerWithTrials(new RegistrationOptions(), new XDrishti.Domain.Lab.TrialCounts(4, 0, 3, 1, 0), 4).ExecuteAsync(FullRun(), Ct);

        verdicts[0].Trials.ShouldNotBeNull().HonestCountApplied.ShouldBeFalse();
        verdicts[1].Trials.ShouldNotBeNull().HonestCountApplied.ShouldBeFalse();
        System.Text.Json.JsonSerializer.Serialize(verdicts[1] with { Trials = null }).ShouldBe(System.Text.Json.JsonSerializer.Serialize(verdicts[0] with { Trials = null }));
    }

    [Fact]
    public async Task A_count_below_the_ledger_total_at_judgement_time_tightens_the_gates_when_rows_are_added_between_two_judgements()
    {
        ArrangeFullTest();
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await RunnerWithTrials(new RegistrationOptions(CountInFactor: true), new XDrishti.Domain.Lab.TrialCounts(2, 0, 3, 0, 0), 2).ExecuteAsync(FullRun(), Ct);
        await RunnerWithTrials(new RegistrationOptions(CountInFactor: true), new XDrishti.Domain.Lab.TrialCounts(2, 0, 3, 2, 0), 2).ExecuteAsync(FullRun(), Ct);

        (verdicts[0].VariantCount, verdicts[1].VariantCount).ShouldBe((5, 7));
        verdicts[1].AdjustmentFactor.ShouldBeGreaterThan(verdicts[0].AdjustmentFactor);
    }

    [Fact]
    public async Task With_hold_out_enforcement_on_a_pass_without_a_kept_hold_out_is_capped_at_marginal_and_with_it_off_nothing_changes()
    {
        ArrangeFullTest();
        var easy = LabSettings.Default with { Gates = new XDrishti.Domain.Lab.QualificationGates(0, -1000, 0, 0, 1000, 1000), MinFoldsForPass = 0 };
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var store = Substitute.For<XDrishti.Application.Abstractions.Lab.IHoldOutStore>();
        var bars = Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IBarQueries>();
        var first = new DateTimeOffset(2026, 2, 2, 9, 15, 0, TimeSpan.FromHours(5.5));
        bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new XDrishti.Application.Abstractions.Market.Bars.BarTotals(2, 1000, first, first.AddMonths(3), 0, 0));

        await RunnerWithTrials(new RegistrationOptions(), null, 1, easy).ExecuteAsync(FullRun(), Ct);
        await RunnerWithTrials(new RegistrationOptions(), null, 1, easy, new HoldOutOptions(Enforce: true), store, bars).ExecuteAsync(FullRun(), Ct);

        verdicts[0].Outcome.ShouldBe("PASS", "the precondition: the relaxed gates pass");
        verdicts[1].Outcome.ShouldBe("MARGINAL");
        verdicts[1].Limits.ShouldContain(l => l.Contains("no hold-out is kept", StringComparison.Ordinal));
        verdicts[1].Gates.ShouldBe(verdicts[0].Gates, "the gates are untouched");
    }

    [Fact]
    public async Task With_a_kept_hold_out_a_pass_stays_a_pass()
    {
        ArrangeFullTest();
        var easy = LabSettings.Default with { Gates = new XDrishti.Domain.Lab.QualificationGates(0, -1000, 0, 0, 1000, 1000), MinFoldsForPass = 0 };
        var verdicts = new List<LabVerdictView>();
        _results.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<LabVerdictView>(verdicts.Add), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var store = Substitute.For<XDrishti.Application.Abstractions.Lab.IHoldOutStore>();
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new XDrishti.Application.Abstractions.Lab.HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 5), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch));
        var bars = Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IBarQueries>();
        var first = new DateTimeOffset(2026, 1, 5, 9, 15, 0, TimeSpan.FromHours(5.5));
        bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new XDrishti.Application.Abstractions.Market.Bars.BarTotals(2, 1000, first, first.AddMonths(10), 0, 0));

        await RunnerWithTrials(new RegistrationOptions(), null, 1, easy, new HoldOutOptions(Enforce: true), store, bars).ExecuteAsync(FullRun(), Ct);

        verdicts.Single().Outcome.ShouldBe("PASS");
    }

    // CF-06 D6: the auto unlock follows the finish, because it reloads the run and needs it stored as succeeded

    private async Task<MemoryHoldOutStore> RunWithAutoUnlockAsync(bool autoOn, string scenario = "succeeded")
    {
        ArrangeFullTest();
        var run = scenario == "quick" ? Run() : FullRun();
        if (scenario == "failed")
        {
            _comparisons.HandleAsync(Arg.Any<RunComparisonQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ComparisonRun>(Error.Validation("x", "stop here")));
        }

        if (scenario == "cancelled")
        {
            _runs.IsCancelRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        }

        var finished = false;
        _runs.FinishAsync(Arg.Any<LabFinish>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _finishes.Add(ci.Arg<LabFinish>());
            finished = true;
            return true;
        });
        // after any finish the stored run reads as succeeded, so only the runner's own gate keeps a failed, cancelled or quick run from an unlock
        _runs.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(_ => run with { State = finished ? LabRunState.Succeeded : LabRunState.Running });
        _results.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(HoldOutUnlockerTests.Verdict());
        var first = new DateTimeOffset(2024, 10, 1, 9, 15, 0, TimeSpan.FromHours(5.5));
        var bars = Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IBarQueries>();
        bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new XDrishti.Application.Abstractions.Market.Bars.BarTotals(2, 1000, first, new DateTimeOffset(2026, 10, 30, 15, 29, 0, TimeSpan.FromHours(5.5)), 0, 0));
        var freezes = Substitute.For<IPlanFreezeStore>();
        freezes.FirstAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new string('f', 64));
        var store = new MemoryHoldOutStore();
        var options = new HoldOutOptions(AutoUnlock: autoOn);
        var unlocker = new HoldOutUnlocker(_runs, _results, store, freezes, new StubPass(), new LabHoldOutGuard(store, bars, options, _time), options, _time,
            Substitute.For<XDrishti.Application.Abstractions.Accounts.IAccountEventLog>(), Substitute.For<XDrishti.Application.Abstractions.Accounts.IAccountUnitOfWork>(), NullLogger<HoldOutUnlocker>.Instance);

        await RunnerWithTrials(new RegistrationOptions(), null, 1, unlocker: unlocker).ExecuteAsync(run, Ct);

        _finishes.Single().State.ShouldBe(scenario switch { "failed" => LabRunState.Failed, "cancelled" => LabRunState.Cancelled, _ => LabRunState.Succeeded });
        return store;
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("quick")]
    public async Task With_the_auto_option_on_a_failed_cancelled_or_quick_run_stores_no_unlock(string scenario)
    {
        var store = await RunWithAutoUnlockAsync(autoOn: true, scenario);

        store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_auto_option_on_and_every_development_row_a_pass_a_succeeded_run_stores_exactly_one_auto_unlock()
    {
        var store = await RunWithAutoUnlockAsync(autoOn: true);

        var unlock = store.Unlocks.ShouldHaveSingleItem();
        unlock.Mode.ShouldBe("auto");
        unlock.Actor.ShouldBe("system:auto-unlock");
        unlock.StrategyId.ShouldBe("LAB_TEST");
    }

    [Fact]
    public async Task With_the_auto_option_off_no_unlock_is_stored()
    {
        var store = await RunWithAutoUnlockAsync(autoOn: false);

        store.Unlocks.ShouldBeEmpty();
    }

    private sealed class StubPass : IHoldOutPass
    {
        public ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(new HoldOutFigures(45, 0.12, block.Start, block.HistoryLast)));
    }
}

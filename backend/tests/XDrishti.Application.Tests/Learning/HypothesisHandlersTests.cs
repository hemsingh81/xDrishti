using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The hypothesis loop use cases (spec P6-10): one knob, the table of moves, the open limit, the one-variable check on stored runs, the judge through the experiment, and that accepting applies nothing.</summary>
public sealed class HypothesisHandlersTests
{
    private static readonly string Base = new('a', 64);

    private static readonly string Cand = new('b', 64);

    private const string Fills = "{\"entry_limit_percent\":0.1,\"slippage\":{\"gap_fill\":\"open\",\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":1}}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly FakeLog _log = new();

    private readonly IExperimentLedger _ledger = Substitute.For<IExperimentLedger>();

    private readonly ICommandHandler<RunExperimentCommand, RunExperimentOutcome> _experiments = Substitute.For<ICommandHandler<RunExperimentCommand, RunExperimentOutcome>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));

    public HypothesisHandlersTests()
    {
        Store(Base, 2, 15);
        Store(Cand, 2.5, 15);
    }

    private static string Record(double rr, double orMin) =>
        $"{{\"strategy\":{{\"id\":\"ORB\",\"parameters\":{{\"or_min\":{orMin.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"rr\":{rr.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},\"definition_json\":\"{{}}\"}}}}";

    private void Store(string key, double rr, double orMin)
    {
        _runs.FindAsync(key, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, key, "sim", 5, 5, "ORB", new string('c', 64), new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, Fills, null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "t"));
        _runs.ReadRecordTextAsync(key, Arg.Any<CancellationToken>()).Returns(Record(rr, orMin));
    }

    private ProposeHypothesisHandler Propose() => new(_runs, _log, _time, NullLogger<ProposeHypothesisHandler>.Instance);

    private MoveHypothesisHandler Move() => new(_log, _ledger, _time, NullLogger<MoveHypothesisHandler>.Instance);

    private StartHypothesisHandler Start() => new(_runs, _log, _time, NullLogger<StartHypothesisHandler>.Instance);

    private JudgeHypothesisHandler Judge() => new(_log, _experiments, _time, NullLogger<JudgeHypothesisHandler>.Instance);

    private static ProposeHypothesisCommand Proposal(string to = "2.5", HypothesisSource source = HypothesisSource.Owner, string actor = "cli:xd", string name = "rr") =>
        new(source, null, new ExperimentKnob(name, ExperimentKnobKind.Parameter, "2", to), "median MFE is 2.7R", Base, new HypothesisGrid(), actor);

    private async Task<long> ProposedAsync()
    {
        var made = await Propose().HandleAsync(Proposal(), Ct);
        return made.Value.Id;
    }

    private async Task<long> ApprovedAsync()
    {
        var id = await ProposedAsync();
        (await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "ok", "cli:xd"), Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    private async Task<long> RunningAsync()
    {
        var id = await ApprovedAsync();
        (await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "cli:xd"), Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    [Fact]
    public async Task A_proposal_is_one_row_with_the_knob_the_baseline_and_nothing_run()
    {
        var made = await Propose().HandleAsync(Proposal(), Ct);

        made.IsSuccess.ShouldBeTrue();
        var row = _log.Rows.Single().Row;
        (row.State, row.FromState, row.Source, row.KnobName, row.BaselineRunKey, row.CandidateRunKey, row.ExperimentId).ShouldBe(("proposed", null, "owner", "rr", Base, null, null));
        made.Value.Text.ShouldContain("Nothing is run");
    }

    [Fact]
    public async Task The_fourth_open_hypothesis_is_refused_and_a_closed_one_frees_a_slot()
    {
        var first = await ProposedAsync();
        await ProposedAsync();
        await ProposedAsync();

        var fourth = await Propose().HandleAsync(Proposal(), Ct);
        await Move().HandleAsync(new MoveHypothesisCommand(first, HypothesisState.Rejected, "no", "cli:xd"), Ct);
        var again = await Propose().HandleAsync(Proposal(), Ct);

        fourth.Error.Code.ShouldBe(HypothesisErrors.OpenLimitCode);
        again.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_parameter_knob_must_exist_in_the_baseline_at_the_stated_value()
    {
        var unknown = await Propose().HandleAsync(Proposal(name: "nope"), Ct);
        var wrong = await Propose().HandleAsync(Proposal() with { Knob = new ExperimentKnob("rr", ExperimentKnobKind.Parameter, "3", "4") }, Ct);

        unknown.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        wrong.Error.Message.ShouldContain("used 2");
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_grid_hypothesis_must_carry_a_grid_value()
    {
        var on = await Propose().HandleAsync(Proposal("2.4", HypothesisSource.Grid), Ct);
        var off = await Propose().HandleAsync(Proposal("2.5", HypothesisSource.Grid), Ct);
        var exit = await Propose().HandleAsync(Proposal(source: HypothesisSource.Grid) with { Knob = new ExperimentKnob("exit", ExperimentKnobKind.Exit, "a", "b") }, Ct);

        on.IsSuccess.ShouldBeTrue();
        off.Error.Message.ShouldContain("grid");
        exit.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        _log.Rows.Single().Row.Source.ShouldBe("grid");
    }

    [Fact]
    public async Task A_bad_proposal_is_refused_before_any_read_and_an_unknown_run_is_not_found()
    {
        var bad = await Propose().HandleAsync(Proposal() with { Rationale = " ", BaselineRunKey = "x", CellKey = "bad", Actor = "" }, Ct);
        var missing = await Propose().HandleAsync(Proposal() with { BaselineRunKey = new string('f', 64) }, Ct);

        bad.Error.Fields!.Select(f => f.Field).ShouldBe(["baselineRunKey", "rationale", "cell", "actor"]);
        await _runs.DidNotReceive().FindAsync(Arg.Is("x"), Arg.Any<CancellationToken>());
        missing.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task At_supervised_autonomy_the_system_cannot_approve_and_the_owner_can()
    {
        var id = await ProposedAsync();

        var system = await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "auto", "system:loop"), Ct);
        var autonomous = await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "auto", "system:loop", AutonomyLevel.Autonomous), Ct);

        system.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
        autonomous.IsSuccess.ShouldBeTrue();
        _log.Rows.Select(r => r.Row.State).ShouldBe(["proposed", "approved"]);
    }

    [Fact]
    public async Task An_illegal_move_and_a_move_to_running_here_are_refused()
    {
        var id = await ProposedAsync();

        var skip = await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Accepted, "x", "cli:xd"), Ct);
        var run = await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Running, "x", "cli:xd"), Ct);
        var unknown = await Move().HandleAsync(new MoveHypothesisCommand(999, HypothesisState.Approved, "x", "cli:xd"), Ct);

        skip.Error.Message.ShouldContain("cannot move to accepted");
        run.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        unknown.Error.Code.ShouldBe(HypothesisErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_system_cannot_start_or_judge_and_a_grid_hypothesis_cannot_use_another_grid()
    {
        var id = await ApprovedAsync();
        var system = await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "system:loop"), Ct);
        var running = await RunningAsyncAfter(id);
        var judge = await Judge().HandleAsync(new JudgeHypothesisCommand(running, new ExperimentOptions(), 6, "system:loop"), Ct);
        var other = await Propose().HandleAsync(Proposal("2.1", HypothesisSource.Grid) with { Grid = new HypothesisGrid(5, 2) }, Ct);

        system.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
        judge.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
        other.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        other.Error.Message.ShouldContain("fixed coarse grid");
    }

    [Fact]
    public async Task A_candidate_without_the_parameter_is_refused_with_a_code_not_an_exception()
    {
        var id = await ApprovedAsync();
        _runs.ReadRecordTextAsync(Cand, Arg.Any<CancellationToken>()).Returns("{\"strategy\":{\"id\":\"ORB\",\"parameters\":{\"or_min\":15},\"definition_json\":\"{}\"}}");

        var started = await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "cli:xd"), Ct);

        started.Error.Code.ShouldBe(HypothesisErrors.NotOneVariableCode);
        started.Error.Message.ShouldContain("candidate run has no parameter 'rr'");
    }

    private async Task<long> RunningAsyncAfter(long approved)
    {
        (await Start().HandleAsync(new StartHypothesisCommand(approved, Cand, "cli:xd"), Ct)).IsSuccess.ShouldBeTrue();
        return approved;
    }

    [Fact]
    public async Task Start_attaches_a_candidate_that_differs_in_the_one_knob()
    {
        var id = await ApprovedAsync();

        var started = await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "cli:xd"), Ct);

        started.IsSuccess.ShouldBeTrue();
        _log.Rows[^1].Row.CandidateRunKey.ShouldBe(Cand);
        _log.Rows[^1].Row.State.ShouldBe("running");
    }

    [Fact]
    public async Task Start_refuses_a_candidate_that_changes_two_knobs_and_leaves_the_hypothesis_approved()
    {
        var id = await ApprovedAsync();
        Store(Cand, 2.5, 20);

        var started = await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "cli:xd"), Ct);

        started.Error.Code.ShouldBe(HypothesisErrors.NotOneVariableCode);
        started.Error.Message.ShouldContain("2 parameters differ");
        _log.Rows[^1].Row.State.ShouldBe("approved");
    }

    [Fact]
    public async Task Start_refuses_an_unchanged_candidate_the_same_run_a_missing_run_and_an_unapproved_hypothesis()
    {
        var proposed = await ProposedAsync();
        var id = await ApprovedAsync();
        Store(Cand, 2, 15);

        var unchanged = await Start().HandleAsync(new StartHypothesisCommand(id, Cand, "cli:xd"), Ct);
        var same = await Start().HandleAsync(new StartHypothesisCommand(id, Base, "cli:xd"), Ct);
        var missing = await Start().HandleAsync(new StartHypothesisCommand(id, new string('f', 64), "cli:xd"), Ct);
        var early = await Start().HandleAsync(new StartHypothesisCommand(proposed, Cand, "cli:xd"), Ct);

        unchanged.Error.Message.ShouldContain("did not change");
        same.Error.Code.ShouldBe(HypothesisErrors.NotOneVariableCode);
        missing.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
        early.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
    }

    [Fact]
    public async Task Judging_runs_the_experiment_and_links_it_without_accepting()
    {
        var id = await RunningAsync();
        _experiments.HandleAsync(Arg.Any<RunExperimentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RunExperimentOutcome(7, false, "Better", 3, [], "report", 5)));

        var judged = await Judge().HandleAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 6, "cli:xd"), Ct);

        judged.IsSuccess.ShouldBeTrue();
        judged.Value.State.ShouldBe("judged");
        _log.Rows[^1].Row.ExperimentId.ShouldBe(7);
        await _experiments.Received(1).HandleAsync(Arg.Is<RunExperimentCommand>(c => c.BaselineRunKey == Base && c.CandidateRunKey == Cand && c.Knob.Name == "rr" && c.HoldOutMonths == 6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refused_experiment_leaves_the_hypothesis_running()
    {
        var id = await RunningAsync();
        _experiments.HandleAsync(Arg.Any<RunExperimentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RunExperimentOutcome>(ExperimentErrors.EntriesAdded(3)));

        var judged = await Judge().HandleAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 6, "cli:xd"), Ct);

        judged.Error.Code.ShouldBe(ExperimentErrors.EntriesAddedCode);
        _log.Rows[^1].Row.State.ShouldBe("running");
    }

    [Fact]
    public async Task Judging_needs_a_running_hypothesis()
    {
        var id = await ApprovedAsync();

        var judged = await Judge().HandleAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 6, "cli:xd"), Ct);

        judged.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
        await _experiments.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Theory]
    [InlineData(ExperimentOutcome.Better, true)]
    [InlineData(ExperimentOutcome.NotBetter, false)]
    [InlineData(ExperimentOutcome.Inconclusive, false)]
    public async Task Only_a_Better_experiment_can_be_accepted_and_accepting_only_records_the_verdict(ExperimentOutcome outcome, bool accepted)
    {
        var id = await RunningAsync();
        _experiments.HandleAsync(Arg.Any<RunExperimentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RunExperimentOutcome(7, false, outcome.ToString(), 1, [], "r", 1)));
        await Judge().HandleAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 6, "cli:xd"), Ct);
        _ledger.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new StoredExperiment(7, Stored(outcome)));

        var result = await Move().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Accepted, "take it", "cli:xd"), Ct);

        result.IsSuccess.ShouldBe(accepted);
        if (accepted)
        {
            result.Value.Text.ShouldContain("Nothing was changed");
        }
        else
        {
            result.Error.Message.ShouldContain("Better");
        }
    }

    [Fact]
    public async Task Hypotheses_are_read_with_their_history_and_the_next_states()
    {
        var id = await ApprovedAsync();

        var one = await new GetHypothesisHandler(_log).HandleAsync(new GetHypothesisQuery(id), Ct);
        var list = await new ListHypothesesHandler(_log).HandleAsync(new ListHypothesesQuery("approved", 10), Ct);
        var bad = await new ListHypothesesHandler(_log).HandleAsync(new ListHypothesesQuery("nope", 0), Ct);
        var none = await new GetHypothesisHandler(_log).HandleAsync(new GetHypothesisQuery(99), Ct);

        one.Value.History.Select(h => h.State).ShouldBe(["proposed", "approved"]);
        one.Value.NextStates.ShouldBe(["running", "rejected", "expired"]);
        one.Value.Note.ShouldContain("changes nothing");
        list.Value.Single().Id.ShouldBe(id);
        bad.Error.Fields!.Count.ShouldBe(2);
        none.Error.Code.ShouldBe(HypothesisErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_grid_query_reads_the_baseline_and_lists_values_without_writing()
    {
        var view = await new GridHypothesesHandler(_runs).HandleAsync(new GridHypothesesQuery(Base, "rr", new HypothesisGrid()), Ct);
        var none = await new GridHypothesesHandler(_runs).HandleAsync(new GridHypothesesQuery(Base, "nope", new HypothesisGrid()), Ct);

        view.Value.Values.ShouldBe(["1.6", "1.8", "2.2", "2.4"]);
        view.Value.Current.ShouldBe("2");
        none.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        _log.Rows.ShouldBeEmpty();
    }

    private static ExperimentToStore Stored(ExperimentOutcome outcome) =>
        new(new string('1', 64), new ExperimentKnob("rr", ExperimentKnobKind.Parameter, "2", "2.5"), Base, Cand, new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), null, outcome, 1, 6, 6, 4, 0.03, 10, 10, 0.1, 0.2, 1, 2, 0.02, 1, string.Empty, "r", "p", DateTimeOffset.UnixEpoch, "t");

    private sealed class FakeLog : IHypothesisLog
    {
        public List<HypothesisRow> Rows { get; } = [];

        private bool IsOpen(long id) => HypothesisLifecycle.IsOpen(HypothesisStateText.TryParse(Latest(id).Row.State)!.Value);

        private HypothesisRow Latest(long id) => Rows.Last(r => r.HypothesisId == id);

        public ValueTask<HypothesisAppend> ProposeAsync(NewHypothesisRow row, int maxOpen, CancellationToken cancellationToken)
        {
            if (Rows.Select(r => r.HypothesisId).Distinct().Count(IsOpen) >= maxOpen)
            {
                return ValueTask.FromResult(new HypothesisAppend(0, HypothesisAppendStatus.OpenLimit));
            }

            var id = Rows.Count + 1L;
            Rows.Add(new HypothesisRow(id, row));
            return ValueTask.FromResult(new HypothesisAppend(id, HypothesisAppendStatus.Created));
        }

        public ValueTask<HypothesisAppend> AppendAsync(NewHypothesisRow row, CancellationToken cancellationToken)
        {
            if (Latest(row.HypothesisId!.Value).Row.State != row.FromState)
            {
                return ValueTask.FromResult(new HypothesisAppend(0, HypothesisAppendStatus.Stale));
            }

            var id = Rows.Count + 1L;
            Rows.Add(new HypothesisRow(id, row));
            return ValueTask.FromResult(new HypothesisAppend(id, HypothesisAppendStatus.Created));
        }

        public ValueTask<HypothesisRow?> FindAsync(long hypothesisId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.Any(r => r.HypothesisId == hypothesisId) ? Latest(hypothesisId) : null);

        public ValueTask<IReadOnlyList<HypothesisRow>> HistoryAsync(long hypothesisId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<HypothesisRow>>([.. Rows.Where(r => r.HypothesisId == hypothesisId)]);

        public ValueTask<IReadOnlyList<HypothesisRow>> ListAsync(string? state, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<HypothesisRow>>([.. Rows.Select(r => r.HypothesisId).Distinct().Select(Latest).Where(r => state is null || r.Row.State == state).Take(limit)]);

        public ValueTask<int> CountOpenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.Select(r => r.HypothesisId).Distinct().Count(IsOpen));
    }
}

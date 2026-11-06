using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 part 1, AC9 (ADR 0221): the lifecycle recorder asks the readiness gate for the two moves it judges and for no other; a refusal leaves no row; a warning leaves the gate's snapshot and failing rows in the row's evidence.</summary>
public sealed class ReadinessLifecycleTests
{
    private const string Cell = KillFixture.Cell;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RecordCellTransitionCommand Move(CellState to, params string[] evidence) =>
        new(Cell, to, "why", "owner:me@x.test", new TransitionFacts(true, true, true, true, 20), evidence.Length == 0 ? ["run:x"] : evidence);

    private static ReadinessGateDecision Decision(bool passed, bool enforced, string gate = "R", long? snapshot = 5) =>
        new(passed, enforced, !passed && enforced, gate, snapshot, passed ? [] : [10, 14], passed ? "gate passed" : "rows not passing: 10 Stability (unavailable)", snapshot is { } id ? [$"readiness-snapshot:{id}", $"readiness-gate-{gate}:{(passed ? "pass" : "fail")}"] : []);

    [Fact]
    public async Task Candidate_to_paper_asks_gate_R_and_the_row_cites_the_snapshot()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        f.Gate.Requests.Clear();
        f.Gate.Decision = Decision(true, false);

        var moved = await f.Record.HandleAsync(Move(CellState.Paper), Ct);

        moved.IsSuccess.ShouldBeTrue();
        f.Gate.Requests.ShouldHaveSingleItem().ShouldBe(new ReadinessGateRequest(Cell, ReadinessGateKind.ResearchToPaper, "owner:me@x.test"));
        f.Log.Rows.Last().Entry.EvidenceIds.ShouldBe(["run:x", "readiness-snapshot:5", "readiness-gate-R:pass"]);
    }

    [Fact]
    public async Task Paper_to_active_asks_gate_S()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate, CellState.Paper);
        f.Gate.Requests.Clear();
        f.Gate.Decision = Decision(true, false, "S");

        var moved = await f.Record.HandleAsync(Move(CellState.Active), Ct);

        moved.IsSuccess.ShouldBeTrue();
        f.Gate.Requests.ShouldHaveSingleItem().Kind.ShouldBe(ReadinessGateKind.PaperToActive);
    }

    [Fact]
    public async Task With_enforce_OFF_a_failed_gate_lets_the_move_through_and_leaves_the_failing_rows_in_the_evidence()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        f.Gate.Decision = Decision(false, false) with { EvidenceIds = ["readiness-snapshot:5", "readiness-gate-R:fail:enforce-off:rows-10.14"] };

        var moved = await f.Record.HandleAsync(Move(CellState.Paper), Ct);

        moved.IsSuccess.ShouldBeTrue();
        f.StateOf(Cell).ShouldBe(CellState.Paper);
        f.Log.Rows.Last().Entry.EvidenceIds.ShouldContain("readiness-gate-R:fail:enforce-off:rows-10.14");
    }

    [Fact]
    public async Task With_enforce_ON_a_failed_gate_refuses_the_move_with_409_code_and_leaves_no_row()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        var rowsBefore = f.Log.Rows.Count;
        f.Gate.Decision = Decision(false, true);

        var moved = await f.Record.HandleAsync(Move(CellState.Paper), Ct);

        moved.IsFailure.ShouldBeTrue();
        moved.Error.Code.ShouldBe("confidence.gate_failed");
        moved.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.Conflict);
        moved.Error.Message.ShouldContain("rows not passing");
        f.Log.Rows.Count.ShouldBe(rowsBefore);
        f.StateOf(Cell).ShouldBe(CellState.Candidate);
    }

    [Fact]
    public async Task A_move_the_table_refuses_never_reaches_the_gate()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        f.Gate.Requests.Clear();

        var moved = await f.Record.HandleAsync(Move(CellState.Active), Ct);

        moved.IsFailure.ShouldBeTrue();
        f.Gate.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Moves_that_are_not_judged_do_not_ask_the_gate()
    {
        var f = new KillFixture();

        (await f.Record.HandleAsync(Move(CellState.Candidate), Ct)).IsSuccess.ShouldBeTrue();
        f.Gate.Requests.ShouldBeEmpty("Research to Candidate is not judged");
        await f.WalkAsync(Cell, CellState.Paper);
        f.Gate.Requests.Count.ShouldBe(1, "Candidate to Paper asks");
        f.Gate.Requests.Clear();

        (await f.Pause.HandleAsync(new PauseCellCommand(Cell, "why", "auto", ["e"]), Ct)).IsSuccess.ShouldBeTrue();

        f.Gate.Requests.ShouldBeEmpty("a pause is never judged");
    }

    [Fact]
    public async Task A_gated_move_with_eighteen_evidence_ids_keeps_all_of_them_and_the_gates_two_make_twenty()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        f.Gate.Decision = Decision(true, false);
        var own = Enumerable.Range(0, 18).Select(i => $"e{i}").ToList();

        var moved = await f.Record.HandleAsync(Move(CellState.Paper, [.. own]), Ct);

        moved.IsSuccess.ShouldBeTrue();
        f.Log.Rows.Last().Entry.EvidenceIds.ShouldBe([.. own, "readiness-snapshot:5", "readiness-gate-R:pass"]);
    }

    [Fact]
    public async Task A_gated_move_with_more_evidence_than_leaves_room_for_the_gates_is_refused_422_before_the_gate_is_asked_and_nothing_is_cut()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate);
        f.Gate.Requests.Clear();
        var rows = f.Log.Rows.Count;

        var moved = await f.Record.HandleAsync(Move(CellState.Paper, [.. Enumerable.Range(0, 19).Select(i => $"e{i}")]), Ct);

        moved.IsFailure.ShouldBeTrue();
        moved.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        moved.Error.Fields.ShouldNotBeNull().ShouldContain(x => x.Field == "evidence");
        f.Gate.Requests.ShouldBeEmpty();
        f.Log.Rows.Count.ShouldBe(rows);
        f.StateOf(Cell).ShouldBe(CellState.Candidate);
    }

    [Fact]
    public async Task A_move_the_gates_do_not_judge_may_still_carry_twenty_evidence_ids()
    {
        var f = new KillFixture();

        var moved = await f.Record.HandleAsync(Move(CellState.Candidate, [.. Enumerable.Range(0, 20).Select(i => $"e{i}")]), Ct);

        moved.IsSuccess.ShouldBeTrue();
        f.Log.Rows.Last().Entry.EvidenceIds.Count.ShouldBe(20);
    }

    [Fact]
    public async Task The_owners_re_enable_of_a_paused_cell_asks_gate_R_and_is_refused_with_enforce_ON_when_it_fails()
    {
        var f = new KillFixture();
        await f.WalkAsync(Cell, CellState.Candidate, CellState.Paper, CellState.Paused);
        f.Gate.Requests.Clear();
        f.Gate.Decision = Decision(false, true);
        var reEnable = new RecordCellTransitionCommand(Cell, CellState.Paper, "back", "owner:me@x.test", new TransitionFacts(OwnerApproved: true, Recovered: true), ["re-enable:no-kill-proposal"]) { OwnerReEnable = true };

        var refused = await f.Record.HandleAsync(reEnable, Ct);

        refused.Error.Code.ShouldBe("confidence.gate_failed");
        f.Gate.Requests.ShouldHaveSingleItem().Kind.ShouldBe(ReadinessGateKind.ResearchToPaper);
        f.StateOf(Cell).ShouldBe(CellState.Paused);

        f.Gate.Decision = Decision(false, false);
        var warned = await f.Record.HandleAsync(reEnable, Ct);

        warned.IsSuccess.ShouldBeTrue();
        f.StateOf(Cell).ShouldBe(CellState.Paper);
    }
}

#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 part 1, AC9 (ADR 0221): the gate warns with the enforce switch OFF and refuses with it ON; it fails closed when the scorecard cannot be built or the switch cannot be read; it stores the snapshot the move cites.</summary>
public sealed class ReadinessGateTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReadinessGateRequest R => new(Cell, ReadinessGateKind.ResearchToPaper, "owner:me@x.test");

    private static ReadinessGateRequest S => new(Cell, ReadinessGateKind.PaperToActive, "owner:me@x.test");

    [Fact]
    public async Task A_passing_gate_R_passes_is_not_refused_and_cites_its_snapshot()
    {
        var f = new ReadinessFixture();

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Passed.ShouldBeTrue();
        decision.Refused.ShouldBeFalse();
        decision.Gate.ShouldBe("R");
        decision.SnapshotId.ShouldBe(1);
        decision.EvidenceIds.ShouldBe(["readiness-snapshot:1", "readiness-gate-R:pass:enforce-off:rows-none"]);
        f.Store.Snapshots.ShouldHaveSingleItem().Scope.ShouldBe("gate");
        f.Store.Snapshots[0].Body.ShouldContain("owner:me@x.test");
    }

    [Fact]
    public async Task With_enforce_OFF_a_failing_gate_is_a_warning_with_the_failing_rows_and_no_refusal()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps() };

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Passed.ShouldBeFalse();
        decision.Enforced.ShouldBeFalse();
        decision.Refused.ShouldBeFalse();
        decision.FailingRows.ShouldBe([10, 11, 14]);
        decision.Message.ShouldContain("rows not passing");
        decision.EvidenceIds[1].ShouldBe("readiness-gate-R:fail:enforce-off:rows-10.11.14");
        decision.SnapshotId.ShouldNotBeNull();
    }

    [Fact]
    public async Task With_enforce_ON_a_failing_gate_is_refused_and_an_unavailable_row_blocks()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps() };
        f.Store.Switch = new ReadinessSwitch(4, true);

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Enforced.ShouldBeTrue();
        decision.Refused.ShouldBeTrue();
        decision.FailingRows.ShouldContain(14);
        f.Store.Snapshots.ShouldHaveSingleItem("the refusal is cited too");
    }

    [Fact]
    public async Task With_enforce_ON_a_passing_gate_is_not_refused()
    {
        var f = new ReadinessFixture();
        f.Store.Switch = new ReadinessSwitch(4, true);

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Passed.ShouldBeTrue();
        decision.Refused.ShouldBeFalse();
    }

    [Fact]
    public async Task Gate_S_also_needs_the_ready_label_and_the_rows_13_and_15()
    {
        var f = new ReadinessFixture();
        f.Store.Switch = new ReadinessSwitch(4, true);
        f.PhaseResult = Result.Failure<PaperPhaseCheckView>(Error.Upstream("x", "down"));

        var decision = await f.Gate.CheckAsync(S, Ct);

        decision.Gate.ShouldBe("S");
        decision.FailingRows.ShouldBe([15]);
        decision.Refused.ShouldBeTrue();
    }

    [Fact]
    public async Task Gate_S_passes_when_every_row_passes_and_the_label_is_ready()
    {
        var f = new ReadinessFixture();

        var decision = await f.Gate.CheckAsync(S, Ct);

        decision.Passed.ShouldBeTrue();
        decision.Gate.ShouldBe("S");
        decision.EvidenceIds[1].ShouldBe("readiness-gate-S:pass:enforce-off:rows-none");
    }

    [Fact]
    public async Task Gate_S_with_a_label_below_ready_names_the_label()
    {
        var f = new ReadinessFixture { Champions = [ReadinessFixture.Champion(6)] };

        var decision = await f.Gate.CheckAsync(S, Ct);

        decision.Passed.ShouldBeFalse();
        decision.FailingRows.ShouldBeEmpty();
        decision.Message.ShouldContain("not Ready for the next phase");
    }

    [Fact]
    public async Task A_switch_that_cannot_be_read_is_read_as_ON()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps() };
        f.Store.SettingsFailure = new InvalidOperationException("db down");

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Enforced.ShouldBeTrue();
        decision.Refused.ShouldBeTrue();
    }

    [Fact]
    public async Task A_scorecard_that_cannot_be_built_is_not_a_pass_and_is_refused_only_with_enforce_ON()
    {
        var off = new ReadinessFixture();
        off.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new InvalidOperationException("db down"));
        var on = new ReadinessFixture();
        on.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new InvalidOperationException("db down"));
        on.Store.Switch = new ReadinessSwitch(4, true);

        var warned = await off.Gate.CheckAsync(R, Ct);
        var refused = await on.Gate.CheckAsync(R, Ct);

        warned.Passed.ShouldBeFalse();
        warned.Refused.ShouldBeFalse();
        warned.SnapshotId.ShouldBeNull();
        warned.Message.ShouldContain("could not be built");
        refused.Refused.ShouldBeTrue();
        off.Store.Snapshots.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_snapshot_that_cannot_be_stored_does_not_change_the_decision()
    {
        var f = new ReadinessFixture();
        f.Store.InsertFailure = new InvalidOperationException("db down");

        var decision = await f.Gate.CheckAsync(R, Ct);

        decision.Passed.ShouldBeTrue();
        decision.SnapshotId.ShouldBeNull();
        decision.EvidenceIds.ShouldBe(["readiness-gate-R:pass:enforce-off:rows-none"]);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var f = new ReadinessFixture();
        f.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () => await f.Gate.CheckAsync(R, Ct));
    }

    [Fact]
    public async Task A_cell_key_that_is_not_a_cell_is_a_gate_that_cannot_be_read()
    {
        var f = new ReadinessFixture();

        var decision = await f.Gate.CheckAsync(new ReadinessGateRequest("nonsense", ReadinessGateKind.ResearchToPaper, "owner:me@x.test"), Ct);

        decision.Passed.ShouldBeFalse();
    }

    [Fact]
    public void Only_a_candidate_to_paper_the_owners_re_enable_and_a_paper_to_active_move_have_a_gate()
    {
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Candidate, XDrishti.Domain.Learning.CellState.Paper).ShouldBe(ReadinessGateKind.ResearchToPaper);
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Paper, XDrishti.Domain.Learning.CellState.Active).ShouldBe(ReadinessGateKind.PaperToActive);
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Paused, XDrishti.Domain.Learning.CellState.Paper).ShouldBe(ReadinessGateKind.ResearchToPaper);
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Paper, XDrishti.Domain.Learning.CellState.Paused).ShouldBeNull();
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Active, XDrishti.Domain.Learning.CellState.Paused).ShouldBeNull();
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Research, XDrishti.Domain.Learning.CellState.Candidate).ShouldBeNull();
    }
}

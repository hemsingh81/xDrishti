using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>
/// CF-12 part 12b, AC6 (ADR 0219): the re-enable is an owner act, Paused to Paper only, and needs the failing condition behind a kill pause to have cleared (every rule that fired holds, none fires; a rule that cannot be evaluated is not a pass). It always carries
/// the CF-11 warning. Nothing but the owner's actor gets through.
/// </summary>
public sealed class ReEnableCellHandlerTests
{
    private const string Owner = "owner:me@x.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A cell paused by an applied kill proposal of the rule, with the report now saying <paramref name="nowState"/> for that rule.</summary>
    private static async Task<KillFixture> PausedByKillAsync(string rule, string nowState)
    {
        var f = new KillFixture { ReportResult = Result.Success(KillFixture.View(KillFixture.Kill(rule, "fires"))) };
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper);
        await f.ProposeAsync();
        (await f.Apply.HandleAsync(new ApplyKillCommand(1, Owner), Ct)).IsSuccess.ShouldBeTrue();
        f.ReportResult = Result.Success(KillFixture.View(KillFixture.Kill(rule, nowState)));
        return f;
    }

    private static ReEnableCellCommand Command(string actor = Owner, string reason = "it recovered") => new(KillFixture.Cell, actor, reason);

    [Fact]
    public async Task When_the_rule_that_fired_holds_now_the_owner_re_enables_to_paper_with_the_gate_warning()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        (result.Value.From, result.Value.To).ShouldBe(("paused", "paper"));
        result.Value.Warnings[0].ShouldStartWith(ReEnableCellHandler.GateWarning);
        result.Value.Warnings[0].ShouldContain("was asked about this re-enable", customMessage: "the fake gate cited nothing on the row, and the warning says so");
        f.Gate.Requests.Last().ShouldBe(new ReadinessGateRequest(KillFixture.Cell, ReadinessGateKind.ResearchToPaper, Owner));
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
        var row = f.Log.Rows[^1];
        (row.Entry.Actor, row.Entry.FromState, row.Entry.ToState).ShouldBe((Owner, "paused", "paper"));
        row.Entry.EvidenceIds.ShouldBe(["cleared-proposal:1"]);
        row.Entry.Reason.ShouldContain("it recovered");
    }

    [Fact]
    public async Task With_enforce_ON_a_gate_R_that_does_not_pass_refuses_the_re_enable_and_the_cell_stays_paused()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        var rows = f.Log.Rows.Count;
        f.Gate.Decision = new ReadinessGateDecision(false, true, true, "R", 5, [10, 14], "rows not passing: 10 Stability (unavailable)", ["readiness-snapshot:5", "readiness-gate-R:fail:enforce-on:rows-10.14"]);

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe("confidence.gate_failed");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
        f.Log.Rows.Count.ShouldBe(rows);
    }

    [Fact]
    public async Task With_enforce_OFF_a_gate_R_that_does_not_pass_lets_the_re_enable_through_and_the_warning_and_the_row_carry_its_result()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        f.Gate.Decision = new ReadinessGateDecision(false, false, false, "R", 5, [10, 14], "rows not passing", ["readiness-snapshot:5", "readiness-gate-R:fail:enforce-off:rows-10.14"]);

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Warnings[0].ShouldStartWith(ReEnableCellHandler.GateWarning);
        result.Value.Warnings[0].ShouldContain("did NOT pass");
        result.Value.Warnings[0].ShouldContain("readiness-gate-R:fail:enforce-off:rows-10.14");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
        f.Log.Rows[^1].Entry.EvidenceIds.ShouldBe(["cleared-proposal:1", "readiness-snapshot:5", "readiness-gate-R:fail:enforce-off:rows-10.14"]);
    }

    [Fact]
    public async Task A_gate_R_that_passes_is_reported_as_passed()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        f.Gate.Decision = new ReadinessGateDecision(true, false, false, "R", 5, [], "passed", ["readiness-snapshot:5", "readiness-gate-R:pass:enforce-off:rows-none"]);

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Value.Warnings[0].ShouldContain("passed for this re-enable");
    }

    [Theory]
    [InlineData("fires")]
    [InlineData("held")]
    [InlineData("not_evaluated")]
    public async Task While_the_rule_still_fires_is_held_or_cannot_be_evaluated_the_cell_stays_paused_with_the_not_cleared_problem(string now)
    {
        var f = await PausedByKillAsync("decay_twice", now);
        var rows = f.Log.Rows.Count;

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(KillErrors.RuleNotClearedCode);
        result.Error.Code.ShouldBe("confidence.rule_not_cleared");
        result.Error.Message.ShouldContain(now);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
        f.Log.Rows.Count.ShouldBe(rows);
    }

    [Fact]
    public async Task Another_rule_that_fires_now_blocks_the_re_enable_even_if_the_original_one_holds()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        f.ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "holds"), KillFixture.Kill("drawdown", "fires")));

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(KillErrors.RuleNotClearedCode);
    }

    [Fact]
    public async Task A_pause_restored_by_a_revert_is_judged_by_the_kill_proposal_that_first_made_it()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        var back = await f.ReEnable.HandleAsync(Command(), Ct);
        var revert = await new RevertChangelogHandler(f.Log, f.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<RevertChangelogHandler>.Instance).HandleAsync(new RevertChangelogCommand(back.Value.ChangelogId, "undo", Owner), Ct);
        f.ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "fires")));

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        revert.IsSuccess.ShouldBeTrue();
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
        result.Error.Code.ShouldBe(KillErrors.RuleNotClearedCode);
    }

    [Theory]
    [InlineData("kill-proposal:99")]
    [InlineData("kill-proposal:zz")]
    public async Task A_pause_whose_kill_proposal_cannot_be_found_stays_paused_and_never_fails_open(string evidence)
    {
        var f = new KillFixture();
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper);
        var paused = await f.Record.HandleAsync(new RecordCellTransitionCommand(KillFixture.Cell, CellState.Paused, "setup", "auto", new TransitionFacts(), [evidence]), Ct);
        paused.IsSuccess.ShouldBeTrue();

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(KillErrors.RuleNotClearedCode);
        result.Error.Message.ShouldContain("cannot be read back");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
    }

    [Fact]
    public async Task A_report_that_cannot_be_read_is_not_a_pass()
    {
        var f = await PausedByKillAsync("cost_stress", "holds");
        f.ReportResult = Result.Failure<DecayReportView>(Error.NotFound("paper.not_found", "no series"));

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(KillErrors.RuleNotClearedCode);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("system")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_an_owner_actor_can_re_enable_and_auto_never_can(string actor)
    {
        var f = await PausedByKillAsync("cost_stress", "holds");

        var result = await f.ReEnable.HandleAsync(Command(actor), Ct);

        result.Error.Code.ShouldBe(KillErrors.OwnerOnlyCode);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
    }

    [Fact]
    public async Task A_cell_that_is_not_paused_has_nothing_to_re_enable()
    {
        var f = new KillFixture();
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper);

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(KillErrors.NotPausedCode);
    }

    [Fact]
    public async Task A_pause_that_no_kill_proposal_made_has_no_condition_to_clear_and_says_so()
    {
        var f = new KillFixture();
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var result = await f.ReEnable.HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.Count.ShouldBe(2);
        result.Value.Warnings.ShouldContain(w => w.Contains("not made by a kill proposal", StringComparison.Ordinal));
        f.Log.Rows[^1].Entry.EvidenceIds.ShouldBe(["re-enable:no-kill-proposal"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_reason_is_required(string reason)
    {
        var f = await PausedByKillAsync("cost_stress", "holds");

        var result = await f.ReEnable.HandleAsync(Command(reason: reason), Ct);

        result.Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
    }

    [Fact]
    public async Task A_bad_cell_key_is_a_learning_invalid_problem()
    {
        var f = new KillFixture();

        var result = await f.ReEnable.HandleAsync(new ReEnableCellCommand("not a cell", Owner, "why"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }

    [Fact]
    public async Task The_switch_needs_the_owner_and_a_reason_and_appends_one_row()
    {
        var store = NSubstitute.Substitute.For<IKillSwitchStore>();
        store.AppendAsync(true, Owner, "I have read two proposals", Ct).Returns(7L);
        var handler = new ChangeKillSwitchHandler(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChangeKillSwitchHandler>.Instance);

        var ok = await handler.HandleAsync(new ChangeKillSwitchCommand(Owner, true, " I have read two proposals "), Ct);
        var notOwner = await handler.HandleAsync(new ChangeKillSwitchCommand("auto", true, "x"), Ct);
        var noReason = await handler.HandleAsync(new ChangeKillSwitchCommand(Owner, true, " "), Ct);
        var longReason = await handler.HandleAsync(new ChangeKillSwitchCommand(Owner, false, new string('x', 501)), Ct);

        ok.Value.ShouldBe(new KillSwitched(7, true));
        notOwner.Error.Code.ShouldBe(KillErrors.OwnerOnlyCode);
        noReason.Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
        longReason.Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
        await store.Received(1).AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>
/// CF-12 part 12b, AC5 (ADR 0219) over the real lifecycle handlers and an in-memory store: any firing rule writes a proposal and a notice; with the setting OFF (default, no row) nothing else changes; ON demotes as actor <c>auto</c> with the setting row and the hash and never
/// as an owner; the CF-09 failure boundary is never executed by auto; a rule that is not evaluated, held or holding never proposes; one evaluation per strategy and day; apply is idempotent and race-safe.
/// </summary>
public sealed class KillProposalFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<KillFixture> TwoPaperCellsAsync(params DecayKillView[] kill)
    {
        var f = new KillFixture { ReportResult = Result.Success(KillFixture.View(kill)) };
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper);
        await f.WalkAsync(KillFixture.OtherCell, CellState.Candidate, CellState.Paper, CellState.Active);
        return f;
    }

    [Fact]
    public async Task With_the_setting_off_a_firing_rule_writes_a_proposal_and_a_notice_and_changes_nothing_else()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        var rows = f.Log.Rows.Count;

        var outcome = await f.ProposeAsync();

        outcome.ProposalIds.ShouldBe([1L]);
        outcome.AppliedAutomatically.ShouldBe(0);
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.Proposal.Rule, row.Proposal.ProposalOnly).ShouldBe(("proposed", null, "cost_stress", false));
        row.Proposal.Cells.ShouldBe([KillFixture.Cell, KillFixture.OtherCell]);
        row.Proposal.SettingsHash.Length.ShouldBe(64);
        f.Log.Rows.Count.ShouldBe(rows, "no cell moved and no changelog row was written");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
        f.StateOf(KillFixture.OtherCell).ShouldBe(CellState.Active);
        await f.Checks.Received(1).InsertAsync(Arg.Any<NewDecayCheck>(), Arg.Any<DateOnly>(), Ct);
        var notice = (await new KillProposalNotices(f.Store).ListAsync(f.Time.GetUtcNow(), Ct)).Single();
        (notice.Id, notice.Kind, notice.Severity).ShouldBe(("kill:1", "kill_proposed", "warning"));
        notice.Text.ShouldContain("cost_stress");
    }

    [Fact]
    public async Task With_the_setting_on_the_cells_are_paused_as_auto_with_the_rule_the_numbers_and_the_setting_and_never_as_an_owner()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        f.Switch = new KillSwitch(5, true);

        var outcome = await f.ProposeAsync();

        outcome.AppliedAutomatically.ShouldBe(1);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
        f.StateOf(KillFixture.OtherCell).ShouldBe(CellState.Paused);
        var pauses = f.Log.Rows.Where(r => r.Entry.ToState == "paused").ToList();
        pauses.Count.ShouldBe(2);
        pauses.ShouldAllBe(r => r.Entry.Actor == "auto");
        pauses.ShouldAllBe(r => r.Entry.EvidenceIds.Single() == "kill-proposal:1");
        pauses.ShouldAllBe(r => r.Entry.BeforeRef == "kill-switch:5" && r.Entry.AfterRef!.StartsWith("settings:", StringComparison.Ordinal));
        var hash = f.Store.Rows[0].Proposal.SettingsHash[..12];
        pauses.ShouldAllBe(r => r.Entry.Reason.Contains("cost_stress", StringComparison.Ordinal) && r.Entry.Reason.Contains("the cost_stress numbers", StringComparison.Ordinal) && r.Entry.Reason.Contains("settings " + hash, StringComparison.Ordinal));
        f.Log.Rows.Any(r => r.Entry.Actor.StartsWith("owner:", StringComparison.Ordinal)).ShouldBeFalse("the owner is never recorded as the approver of an automatic decision");
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.AutoSettingId).ShouldBe(("applied", "auto", 5L));
        var notices = await new KillProposalNotices(f.Store).ListAsync(f.Time.GetUtcNow(), Ct);
        notices.Select(n => (n.Id, n.Kind, n.Resolved)).ShouldBe([("kill-auto:1", "kill_applied_auto", true)], "the open notice is gone, and the automatic pause stays on the bell as a resolved notice");
    }

    [Fact]
    public async Task An_automatic_pause_stays_on_the_bell_for_three_days_an_owners_does_not()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        f.Switch = new KillSwitch(5, true);
        await f.ProposeAsync();
        var notices = new KillProposalNotices(f.Store);

        var day2 = await notices.ListAsync(f.Time.GetUtcNow().AddDays(2), Ct);
        var day4 = await notices.ListAsync(f.Time.GetUtcNow().AddDays(4), Ct);
        f.Store.Rows[0] = f.Store.Rows[0] with { Actor = "owner:me@x.test", AutoSettingId = null };
        var byOwner = await notices.ListAsync(f.Time.GetUtcNow().AddDays(2), Ct);

        day2.Single().Text.ShouldContain("automatic setting");
        day4.ShouldBeEmpty();
        byOwner.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_failure_boundary_makes_a_proposal_that_the_automatic_setting_never_executes()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("failure_boundary", "fires", proposalOnly: true));
        f.Switch = new KillSwitch(5, true);

        var outcome = await f.ProposeAsync();
        var direct = await f.Apply.HandleAsync(new ApplyKillCommand(1, KillActors.Auto), Ct);

        outcome.ProposalIds.ShouldBe([1L]);
        outcome.AppliedAutomatically.ShouldBe(0);
        direct.Error.Code.ShouldBe(KillErrors.ProposalOnlyCode);
        f.Store.Rows.Single().State.ShouldBe("proposed");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
        f.Log.Rows.Any(r => r.Entry.ToState == "paused").ShouldBeFalse();
    }

    [Theory]
    [InlineData("not_evaluated")]
    [InlineData("held")]
    [InlineData("holds")]
    public async Task A_rule_that_is_not_evaluated_held_or_holding_never_makes_a_proposal_or_a_demotion(string state)
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", state), KillFixture.Kill("drawdown", state), KillFixture.Kill("decay_twice", state));
        f.Switch = new KillSwitch(5, true);

        var outcome = await f.ProposeAsync();

        outcome.ProposalIds.ShouldBeEmpty();
        f.Store.Rows.ShouldBeEmpty();
        f.Log.Rows.Any(r => r.Entry.ToState == "paused").ShouldBeFalse();
        await f.Checks.Received(1).InsertAsync(Arg.Any<NewDecayCheck>(), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_strategy_is_evaluated_once_a_day_and_an_open_proposal_is_not_proposed_twice()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("decay_twice", "fires"));
        await f.ProposeAsync();

        f.CheckExists = true;
        var again = await f.ProposeAsync();
        f.CheckExists = false;
        f.Time.Advance(TimeSpan.FromDays(1));
        var nextDay = await f.ProposeAsync();

        again.AlreadyChecked.ShouldBeTrue();
        nextDay.ProposalIds.ShouldBeEmpty();
        nextDay.FiringWithoutProposal.ShouldBe(1);
        f.Store.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_firing_rule_with_no_cell_to_pause_makes_no_proposal()
    {
        var f = new KillFixture { ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "fires"))) };
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var outcome = await f.ProposeAsync();

        outcome.ProposalIds.ShouldBeEmpty();
        outcome.FiringWithoutProposal.ShouldBe(1);
        f.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_report_leaves_everything_unchanged_and_records_nothing_for_the_day()
    {
        var f = await TwoPaperCellsAsync();
        f.ReportResult = Result.Failure<DecayReportView>(Error.Validation("paper.invalid", "no"));
        f.Switch = new KillSwitch(5, true);

        var result = await f.Propose.HandleAsync(new ProposeKillCommand("ORB"), Ct);

        result.IsFailure.ShouldBeTrue();
        f.Store.Rows.ShouldBeEmpty();
        await f.Checks.DidNotReceive().InsertAsync(Arg.Any<NewDecayCheck>(), Arg.Any<DateOnly>(), Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A|B")]
    public async Task A_missing_strategy_or_one_with_a_pipe_is_refused(string? strategy)
    {
        var f = new KillFixture();

        var result = await f.Propose.HandleAsync(new ProposeKillCommand(strategy), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_owner_applies_a_proposal_and_a_second_apply_changes_nothing()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("drawdown", "fires"));
        await f.ProposeAsync();

        var first = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);
        var rows = f.Log.Rows.Count;
        var second = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        first.Value.PausedCells.Count.ShouldBe(2);
        first.Value.AlreadyApplied.ShouldBeFalse();
        second.Value.AlreadyApplied.ShouldBeTrue();
        f.Log.Rows.Count.ShouldBe(rows);
        f.Log.Rows.Where(r => r.Entry.ToState == "paused").ShouldAllBe(r => r.Entry.Actor == "owner:me@x.test" && r.Entry.BeforeRef == null);
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.AutoSettingId).ShouldBe(("applied", "owner:me@x.test", null));
        f.Store.Decisions.ShouldBe(1);
    }

    [Fact]
    public async Task The_owner_can_apply_the_failure_boundary_proposal()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("failure_boundary", "fires", proposalOnly: true));
        await f.ProposeAsync();

        var applied = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        applied.IsSuccess.ShouldBeTrue();
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
    }

    [Fact]
    public async Task An_automatic_apply_while_the_setting_is_off_is_refused_and_changes_nothing()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("drawdown", "fires"));
        await f.ProposeAsync();

        var result = await f.Apply.HandleAsync(new ApplyKillCommand(1, KillActors.Auto), Ct);

        result.Error.Code.ShouldBe(KillErrors.AutoOffCode);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
        f.Store.Rows.Single().State.ShouldBe("proposed");
    }

    [Theory]
    [InlineData("system")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_the_owner_or_auto_can_apply(string actor)
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("drawdown", "fires"));
        await f.ProposeAsync();

        var result = await f.Apply.HandleAsync(new ApplyKillCommand(1, actor), Ct);

        result.Error.Code.ShouldBe(KillErrors.OwnerOnlyCode);
    }

    [Fact]
    public async Task An_unknown_proposal_is_not_found_and_a_dismissed_one_is_closed()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("drawdown", "fires"));
        await f.ProposeAsync();
        f.Store.Rows[0] = f.Store.Rows[0] with { State = "dismissed", Actor = "owner:me@x.test" };

        var missing = await f.Apply.HandleAsync(new ApplyKillCommand(99, "owner:me@x.test"), Ct);
        var closed = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        missing.Error.Code.ShouldBe(KillErrors.ProposalNotFoundCode);
        closed.Error.Code.ShouldBe(KillErrors.ProposalClosedCode);
    }

    [Fact]
    public async Task When_another_caller_wins_the_race_this_one_is_idempotent_and_writes_no_second_pause()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        await f.ProposeAsync();
        f.Store.BeforeDecide = id =>
        {
            // the other caller decided between this call's pauses and its compare-and-set
            f.Store.BeforeDecide = null;
            var i = f.Store.Rows.FindIndex(r => r.Id == id);
            f.Store.Rows[i] = f.Store.Rows[i] with { State = "applied", Actor = "owner:other@x.test" };
        };

        var result = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AlreadyApplied.ShouldBeTrue();
        result.Value.Actor.ShouldBe("owner:other@x.test");
        f.Log.Rows.Count(r => r.Entry.ToState == "paused").ShouldBe(2, "one pause row per cell, not two");
    }

    [Fact]
    public async Task A_cell_that_a_concurrent_call_paused_first_is_skipped_not_failed()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        await f.ProposeAsync();
        await f.WalkAsync(KillFixture.Cell, CellState.Paused);

        var result = await f.Apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        result.Value.SkippedCells.ShouldBe([KillFixture.Cell]);
        result.Value.PausedCells.ShouldBe([KillFixture.OtherCell]);
    }

    [Fact]
    public async Task A_failed_pause_marks_nothing_applied_and_a_retry_pauses_the_rest()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        await f.ProposeAsync();
        var flaky = new FailingSecondPause(f.Pause);
        var apply = new ApplyKillHandler(f.Store, f.Store, f.Settings, f.Cells, flaky, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplyKillHandler>.Instance);

        var failed = await apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);
        var stateAfterFailure = f.Store.Rows.Single().State;
        var retried = await apply.HandleAsync(new ApplyKillCommand(1, "owner:me@x.test"), Ct);

        failed.IsFailure.ShouldBeTrue();
        stateAfterFailure.ShouldBe("proposed");
        retried.Value.SkippedCells.ShouldBe([KillFixture.Cell]);
        retried.Value.PausedCells.ShouldBe([KillFixture.OtherCell]);
        f.Store.Rows.Single().State.ShouldBe("applied");
        f.Log.Rows.Count(r => r.Entry.ToState == "paused").ShouldBe(2);
    }

    [Fact]
    public async Task An_automatic_apply_refused_by_the_record_is_a_failure_not_an_exception()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        await f.ProposeAsync();
        f.Switch = new KillSwitch(5, true);
        f.Store.Refuse = true; // the switch was turned OFF between the check and the update

        var result = await f.Apply.HandleAsync(new ApplyKillCommand(1, KillActors.Auto), Ct);

        result.Error.Code.ShouldBe(KillErrors.DecisionRefusedCode);
        f.Store.Rows.Single().State.ShouldBe("proposed");
    }

    [Fact]
    public async Task An_older_open_proposal_is_applied_by_auto_only_while_its_rule_still_fires_today()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        await f.ProposeAsync(); // open since today, the setting is OFF
        f.Switch = new KillSwitch(5, true);
        f.Time.Advance(TimeSpan.FromDays(1));
        f.ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "holds")));

        var holds = await f.ProposeAsync();
        f.Time.Advance(TimeSpan.FromDays(1));
        f.ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "fires")));
        var fires = await f.ProposeAsync();

        holds.AppliedAutomatically.ShouldBe(0, "the rule no longer fires: the old proposal stays for the owner");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
        fires.AppliedAutomatically.ShouldBe(1);
        fires.ProposalIds.ShouldBeEmpty("the open proposal is the one applied, no second one is made");
        f.Store.Rows.Single().Actor.ShouldBe("auto");
    }

    [Fact]
    public async Task An_automatic_apply_that_throws_is_counted_and_retried_on_a_rerun_of_the_same_day()
    {
        var f = await TwoPaperCellsAsync(KillFixture.Kill("cost_stress", "fires"));
        f.Switch = new KillSwitch(5, true);
        var apply = Substitute.For<XDrishti.Application.Abstractions.Messaging.ICommandHandler<ApplyKillCommand, ApplyKillOutcome>>();
        apply.HandleAsync(Arg.Any<ApplyKillCommand>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("the store is down"),
            _ => ValueTask.FromResult(Result.Success(new ApplyKillOutcome(1, "ORB", "cost_stress", "applied", false, [], [], KillActors.Auto))));
        var propose = new ProposeKillHandler(f.Report, f.Store, f.Store, f.Checks, f.Settings, f.Cells, apply, new Domain.Confidence.DecayOptions(), new Domain.Confidence.KillOptions(), f.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProposeKillHandler>.Instance);

        var first = await propose.HandleAsync(new ProposeKillCommand("ORB"), Ct);
        f.CheckExists = true; // the evaluation of the day was recorded
        var rerun = await propose.HandleAsync(new ProposeKillCommand("ORB"), Ct);

        first.Value.ApplyFailures.ShouldBe(1);
        first.Value.AppliedAutomatically.ShouldBe(0);
        rerun.Value.AlreadyChecked.ShouldBeTrue();
        rerun.Value.AppliedAutomatically.ShouldBe(1, "the proposal made today is still open and the apply is tried again");
    }

    /// <summary>The first call goes through; the second call fails once (a store that is down for a moment).</summary>
    private sealed class FailingSecondPause(XDrishti.Application.Abstractions.Messaging.ICommandHandler<PauseCellCommand, ChangelogOutcome> inner) : XDrishti.Application.Abstractions.Messaging.ICommandHandler<PauseCellCommand, ChangelogOutcome>
    {
        private int _calls;

        public ValueTask<Result<ChangelogOutcome>> HandleAsync(PauseCellCommand command, CancellationToken cancellationToken) =>
            ++_calls == 2 ? ValueTask.FromResult(Result.Failure<ChangelogOutcome>(Error.Conflict("changelog.stale", "down"))) : inner.HandleAsync(command, cancellationToken);
    }
}

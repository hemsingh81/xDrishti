using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Autonomy;
using XDrishti.Application.Planning;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-10 over fake stores: the owner's approve / modify / reject / approve-all go through the stored checklist, the cutoff and the owner-only guard; the review job lapses at the cutoff and never calls the auto path while the setting is OFF.</summary>
public sealed class ReviewHandlersTests
{
    private const string Owner = "owner:hem@example.com";

    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private static readonly Guid AccountA = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a01");

    private static readonly Guid AccountB = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a02");

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private readonly ICommandHandler<MoveTicketCommand, TicketMoveOutcome> _mover = Substitute.For<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>();

    private readonly FakeTimeProvider _time = new(Cutoff.AddHours(-10));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanItemRow Item(long id, string[]? standings = null, TicketGroup group = TicketGroup.Primary, TicketState state = TicketState.Proposed, bool wired = true, params Guid[] accounts) =>
        new(id, 1, new NewPlanItem(
            (int)id, group, "ABC", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"],
            new PlanEvidence("c", 30, "r", "b", 0.6, null, [.. standings ?? [], .. wired ? [PlanWiring.IndicativeEntry] : (string[])[]], null, 0.7),
            [.. (accounts.Length == 0 ? [AccountA] : accounts).Select(a => new NewAllocation(a, "paper", 10))]), state);

    private static PlanRow Plan(PlanState state = PlanState.Published) =>
        new(1, new NewPlan("k", new DateOnly(2026, 10, 26), Cutoff, 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), state, 2, 0, 0);

    private ApproveTicketHandler Approver() => new(_plans, new StoredChecklistSource(), _mover, _time, NullLogger<ApproveTicketHandler>.Instance);

    private void Arrange(PlanState state, params PlanItemRow[] items)
    {
        _plans.FindAsync(1, Ct).Returns(Plan(state));
        foreach (var item in items)
        {
            _plans.FindItemAsync(item.Id, Ct).Returns(item);
        }

        _plans.ItemsAsync(1, Ct).Returns(items);
        _plans.ApprovalsAsync(1, Ct).Returns([]);
        _mover.HandleAsync(Arg.Any<MoveTicketCommand>(), Ct).Returns(Result.Success(new TicketMoveOutcome(1, "approved")));
    }

    [Fact]
    public async Task A_ticket_whose_checks_all_passed_is_approved_through_the_owner_move_with_the_watching_notice()
    {
        Arrange(PlanState.Published, Item(5));

        var result = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);

        result.Value.Notice.ShouldBe("Approved for watching: no order is placed and none exists.");
        await _mover.Received(1).HandleAsync(Arg.Is<MoveTicketCommand>(m => m.ItemId == 5 && m.To == TicketState.Approved && m.Actor == Owner && m.ConfirmReason == null), Ct);
    }

    [Theory]
    [InlineData("rule:autonomy")]
    [InlineData("system:review-cutoff")]
    [InlineData("hem@example.com")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_an_owner_actor_can_approve_modify_reject_or_approve_all(string actor)
    {
        Arrange(PlanState.Published, Item(5));

        var approve = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, actor, null, null), Ct);
        var reject = await new RejectTicketHandler(_plans, _mover, NullLogger<RejectTicketHandler>.Instance).HandleAsync(new RejectTicketCommand(1, 5, actor, "no"), Ct);
        var all = await new ApproveAllHandler(_plans, new StoredChecklistSource(), _mover, _time, NullLogger<ApproveAllHandler>.Instance).HandleAsync(new ApproveAllCommand(1, actor), Ct);

        approve.Error.Code.ShouldBe(PlanErrors.OwnerOnlyCode);
        reject.Error.Code.ShouldBe(PlanErrors.OwnerOnlyCode);
        all.Error.Code.ShouldBe(PlanErrors.OwnerOnlyCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unavailable_check_blocks_the_approval_until_the_owner_names_its_code_with_a_reason()
    {
        Arrange(PlanState.Published, Item(5, ["checklist:data_quality:unavailable"]));

        var plain = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);
        var confirmed = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, [new CheckConfirmation(AccountA, [ChecklistCodes.DataQuality], "price feed checked by hand")]), Ct);

        plain.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        plain.Error.Message.ShouldContain("data_quality");
        confirmed.Value.OverriddenCodes.ShouldBe([ChecklistCodes.DataQuality]);
        await _mover.Received(1).HandleAsync(Arg.Is<MoveTicketCommand>(m => m.ConfirmReason!.StartsWith("[" + AccountA + "] overrode data_quality: price feed checked by hand", StringComparison.Ordinal)), Ct);
    }

    [Fact]
    public async Task A_confirmation_for_another_account_or_another_check_covers_nothing()
    {
        Arrange(PlanState.Published, Item(5, ["checklist:data_quality:unavailable"]));

        var otherAccount = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, [new CheckConfirmation(AccountB, [ChecklistCodes.DataQuality], "elsewhere")]), Ct);
        var otherCheck = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, [new CheckConfirmation(AccountA, [ChecklistCodes.FnoBan], "wrong one")]), Ct);

        otherAccount.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        otherCheck.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Every_account_of_the_ticket_needs_its_own_confirmation()
    {
        Arrange(PlanState.Published, Item(5, ["checklist:data_quality:unavailable"], accounts: [AccountA, AccountB]));

        var one = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, [new CheckConfirmation(AccountA, [ChecklistCodes.DataQuality], "a")]), Ct);
        var both = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, [new CheckConfirmation(AccountA, [ChecklistCodes.DataQuality], "a"), new CheckConfirmation(AccountB, [ChecklistCodes.DataQuality], "b")]), Ct);

        one.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        both.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_ticket_with_no_stored_checklist_or_an_unreadable_one_cannot_be_approved_even_with_a_confirmation()
    {
        Arrange(PlanState.Published, Item(5, wired: false), Item(6, ["checklist:made_up:fail"]));
        var confirm = new[] { new CheckConfirmation(AccountA, [ChecklistCodes.DataQuality], "trust me") };

        var unwired = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, confirm), Ct);
        var unreadable = await Approver().HandleAsync(new ApproveTicketCommand(1, 6, Owner, null, confirm), Ct);

        unwired.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        unreadable.Error.Code.ShouldBe(PlanErrors.ChecklistBlockedCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task After_the_cutoff_or_in_a_closed_plan_an_approval_and_a_modification_are_refused()
    {
        Arrange(PlanState.Published, Item(5));
        _time.SetUtcNow(Cutoff);

        var late = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);
        var lateModify = await new ModifyTicketHandler(_plans, new StoredChecklistSource(), _mover, _time, NullLogger<ModifyTicketHandler>.Instance).HandleAsync(new ModifyTicketCommand(1, 5, Owner, "less", [new QuantityChange(AccountA, 5)]), Ct);

        late.Error.Code.ShouldBe(PlanErrors.ClosedCode);
        lateModify.Error.Code.ShouldBe(PlanErrors.ClosedCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task In_a_plan_that_is_no_longer_in_review_an_approval_is_refused_even_before_the_cutoff_time()
    {
        Arrange(PlanState.Lapsed, Item(5));

        var closed = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);

        closed.Error.Code.ShouldBe(PlanErrors.ClosedCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_second_approval_of_the_same_ticket_is_refused_by_the_table_and_writes_nothing()
    {
        Arrange(PlanState.InReview, Item(5, state: TicketState.Approved));

        var again = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);

        again.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_ticket_of_another_plan_is_not_found_in_this_one()
    {
        Arrange(PlanState.Published);
        _plans.FindItemAsync(9, Ct).Returns(Item(9) with { PlanId = 2 });

        var result = await Approver().HandleAsync(new ApproveTicketCommand(1, 9, Owner, null, null), Ct);

        result.Error.Code.ShouldBe(PlanErrors.ItemNotFoundCode);
    }

    [Fact]
    public async Task A_modification_only_lowers_a_size_and_leaves_the_ticket_modified_not_approved()
    {
        Arrange(PlanState.Published, Item(5, accounts: [AccountA]));
        var handler = new ModifyTicketHandler(_plans, new StoredChecklistSource(), _mover, _time, NullLogger<ModifyTicketHandler>.Instance);

        var higher = await handler.HandleAsync(new ModifyTicketCommand(1, 5, Owner, "more", [new QuantityChange(AccountA, 11)]), Ct);
        var unknown = await handler.HandleAsync(new ModifyTicketCommand(1, 5, Owner, "other", [new QuantityChange(AccountB, 1)]), Ct);
        var lower = await handler.HandleAsync(new ModifyTicketCommand(1, 5, Owner, "less", [new QuantityChange(AccountA, 4)]), Ct);

        higher.Error.Code.ShouldBe(TicketModification.InvalidCode);
        unknown.Error.Code.ShouldBe(TicketModification.InvalidCode);
        lower.Value.State.ShouldBe("modified");
        await _mover.Received(1).HandleAsync(Arg.Is<MoveTicketCommand>(m => m.To == TicketState.Modified && m.Modifications == "qty:" + AccountA + "=4"), Ct);
    }

    [Fact]
    public async Task Approving_a_modified_ticket_checks_the_stored_change_again()
    {
        var item = Item(5, state: TicketState.Modified);
        Arrange(PlanState.InReview, item);
        _plans.ApprovalsAsync(1, Ct).Returns([new ApprovalRow(1, 5, "owner", "modify", Owner, "less", null, "qty:" + AccountA + "=40", Cutoff)]);

        var result = await Approver().HandleAsync(new ApproveTicketCommand(1, 5, Owner, null, null), Ct);

        result.Error.Code.ShouldBe(TicketModification.InvalidCode);
        await _mover.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Approve_all_takes_only_primary_proposed_tickets_with_every_check_passed_and_lists_the_rest()
    {
        Arrange(
            PlanState.Published,
            Item(1),
            Item(2, ["checklist:data_quality:unavailable"]),
            Item(3, ["checklist:holdings_conflict:fail"]),
            Item(4, group: TicketGroup.Reserve),
            Item(5, state: TicketState.Modified),
            Item(6, wired: false));
        var all = new ApproveAllHandler(_plans, new StoredChecklistSource(), _mover, _time, NullLogger<ApproveAllHandler>.Instance);

        var result = await all.HandleAsync(new ApproveAllCommand(1, Owner), Ct);

        result.Value.Approved.ShouldBe([1]);
        result.Value.Skipped.Select(s => s.ItemId).ShouldBe([2, 3, 5, 6]);
        result.Value.Skipped.Single(s => s.ItemId == 2).Reason.ShouldContain("data_quality");
        await _mover.Received(1).HandleAsync(Arg.Any<MoveTicketCommand>(), Ct);
        await _mover.Received(1).HandleAsync(Arg.Is<MoveTicketCommand>(m => m.ItemId == 1 && m.ConfirmReason == null), Ct);
    }

    [Fact]
    public async Task Rejecting_after_the_cutoff_time_is_allowed_by_the_handler()
    {
        Arrange(PlanState.Published, Item(5));
        _time.SetUtcNow(Cutoff.AddMinutes(1));
        _mover.HandleAsync(Arg.Any<MoveTicketCommand>(), Ct).Returns(Result.Success(new TicketMoveOutcome(5, "rejected")));

        var result = await new RejectTicketHandler(_plans, _mover, NullLogger<RejectTicketHandler>.Instance).HandleAsync(new RejectTicketCommand(1, 5, Owner, "no thanks"), Ct);

        result.Value.State.ShouldBe("rejected");
    }

    private (RunReviewCycleHandler Handler, ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome> Auto, ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome> Cutoff, IAutonomySettingsStore Settings) Cycle(bool autoOn)
    {
        var settings = Substitute.For<IAutonomySettingsStore>();
        settings.CurrentAsync(Ct).Returns(new AutonomySettings(7, new string('a', 64), autoOn, false, 0.58, 3));
        var auto = Substitute.For<ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>>();
        auto.HandleAsync(Arg.Any<AutoApprovePlanCommand>(), Ct).Returns(Result.Success(new AutoApprovalOutcome(1, true, 1, [])));
        var cutoff = Substitute.For<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>();
        cutoff.HandleAsync(Arg.Any<ApplyReviewCutoffCommand>(), Ct).Returns(Result.Success(new PlanMoveOutcome(1, "lapsed", 3)));
        _plans.ListAsync(new DateOnly(2026, 10, 26), null, Arg.Any<int>(), Ct).Returns([Plan()]);
        return (new RunReviewCycleHandler(_plans, settings, auto, cutoff, _time, NullLogger<RunReviewCycleHandler>.Instance), auto, cutoff, settings);
    }

    [Fact]
    public async Task At_the_cutoff_the_job_lapses_and_with_the_setting_off_never_reads_or_calls_the_auto_path()
    {
        var (handler, auto, cutoff, settings) = Cycle(autoOn: false);
        _time.SetUtcNow(Cutoff);

        var result = await handler.HandleAsync(new RunReviewCycleCommand(), Ct);

        result.Value.Lapsed.ShouldBe(3);
        result.Value.AutoApproved.ShouldBe(0);
        await cutoff.Received(1).HandleAsync(Arg.Is<ApplyReviewCutoffCommand>(c => c.PlanId == 1 && c.Actor == "system:review-cutoff"), Ct);
        await auto.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await settings.DidNotReceiveWithAnyArgs().CurrentAsync(Ct);
    }

    [Fact]
    public async Task Before_the_cutoff_with_the_setting_off_the_job_does_nothing_at_all()
    {
        var (handler, auto, cutoff, settings) = Cycle(autoOn: false);
        _time.SetUtcNow(Cutoff.AddMinutes(-1));

        var result = await handler.HandleAsync(new RunReviewCycleCommand(), Ct);

        result.Value.AutoApproved.ShouldBe(0);
        await cutoff.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await auto.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await settings.Received(1).CurrentAsync(Ct);
    }

    [Fact]
    public async Task With_the_setting_on_the_auto_pass_runs_only_in_the_last_minutes_before_the_cutoff_and_never_at_or_after_it()
    {
        var (handler, auto, cutoff, _) = Cycle(autoOn: true);

        _time.SetUtcNow(Cutoff.AddMinutes(-10));
        await handler.HandleAsync(new RunReviewCycleCommand(), Ct);
        await auto.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);

        _time.SetUtcNow(Cutoff.AddMinutes(-1));
        var inside = await handler.HandleAsync(new RunReviewCycleCommand(), Ct);
        await auto.Received(1).HandleAsync(Arg.Is<AutoApprovePlanCommand>(c => c.PlanId == 1), Ct);
        await cutoff.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);

        _time.SetUtcNow(Cutoff);
        await handler.HandleAsync(new RunReviewCycleCommand(), Ct);
        await auto.Received(1).HandleAsync(Arg.Any<AutoApprovePlanCommand>(), Ct);
        await cutoff.Received(1).HandleAsync(Arg.Any<ApplyReviewCutoffCommand>(), Ct);
        inside.Value.AutoApproved.ShouldBe(1);
    }

    [Fact]
    public async Task A_closed_plan_or_no_plan_is_a_success_that_does_nothing()
    {
        var (handler, auto, cutoff, _) = Cycle(autoOn: true);
        _time.SetUtcNow(Cutoff.AddMinutes(5));
        _plans.ListAsync(new DateOnly(2026, 10, 26), null, Arg.Any<int>(), Ct).Returns([Plan(PlanState.Lapsed)]);

        var closed = await handler.HandleAsync(new RunReviewCycleCommand(), Ct);
        _plans.ListAsync(new DateOnly(2026, 10, 26), null, Arg.Any<int>(), Ct).Returns([]);
        var none = await handler.HandleAsync(new RunReviewCycleCommand(), Ct);

        closed.IsSuccess.ShouldBeTrue();
        none.IsSuccess.ShouldBeTrue();
        await cutoff.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await auto.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }
}

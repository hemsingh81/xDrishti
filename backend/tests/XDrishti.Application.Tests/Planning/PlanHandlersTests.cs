using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The plan use cases (spec P7-03) over a fake store: refusals come before any write, the owner is the only one who decides, the cutoff lapses and never approves.</summary>
public sealed class PlanHandlersTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private readonly IPlanStore _store = Substitute.For<IPlanStore>();

    private FakeTimeProvider _time = new(Cutoff.AddMinutes(1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanItemRow Item(long id, TicketState state, TicketGroup group = TicketGroup.Primary) =>
        new(id, 1, new NewPlanItem((int)id, group, "ABC", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"], new PlanEvidence("c", 30, "r", "b", null, null, [], null), []), state);

    private static PlanRow Plan(PlanState state) =>
        new(1, new NewPlan("k", new DateOnly(2026, 10, 26), Cutoff, 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), state, 1, 0, 0);

    private MoveTicketHandler Ticket() => new(_store, _time, NullLogger<MoveTicketHandler>.Instance);

    private MovePlanHandler PlanMove() => new(_store, _time, NullLogger<MovePlanHandler>.Instance);

    private ApplyReviewCutoffHandler Cutoffs() => new(_store, _time, NullLogger<ApplyReviewCutoffHandler>.Instance);

    [Fact]
    public async Task The_owner_approving_writes_the_move_with_an_owner_approval()
    {
        _store.FindItemAsync(7, Ct).Returns(Item(7, TicketState.Proposed));
        _store.MoveItemAsync(Arg.Any<ItemMove>(), Ct).Returns(MoveStatus.Created);

        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, TicketState.Approved, "owner:me", "looks fine", ConfirmReason: "known gap"), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _store.Received(1).MoveItemAsync(Arg.Is<ItemMove>(m => m.From == TicketState.Proposed && m.To == TicketState.Approved && m.Approval!.Decision == ApprovalDecision.Approve && m.Approval.ConfirmReason == "known gap"), Ct);
    }

    [Fact]
    public async Task An_approval_after_the_plans_cutoff_is_refused_by_the_store_and_the_handler_says_so()
    {
        _store.FindItemAsync(7, Ct).Returns(Item(7, TicketState.Proposed));
        _store.MoveItemAsync(Arg.Any<ItemMove>(), Ct).Returns(MoveStatus.AfterCutoff);

        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, TicketState.Approved, "owner:me", "late"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        result.Error.Message.ShouldContain("cutoff");
    }

    [Theory]
    [InlineData("cli:xd")]
    [InlineData("system:nightly")]
    [InlineData("ownerx")]
    public async Task Only_an_owner_prefixed_actor_decides(string actor)
    {
        _store.FindItemAsync(7, Ct).Returns(Item(7, TicketState.Proposed));

        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, TicketState.Approved, actor, "x"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        await _store.DidNotReceive().MoveItemAsync(Arg.Any<ItemMove>(), Ct);
    }

    [Fact]
    public async Task A_plan_move_the_store_finds_unsupported_by_the_tickets_is_refused()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.InReview));
        _store.ItemsAsync(1, Ct).Returns([Item(1, TicketState.Approved)]);
        _store.MovePlanAsync(Arg.Any<PlanMove>(), Ct).Returns(MoveStatus.Refused);

        var result = await PlanMove().HandleAsync(new MovePlanCommand(1, PlanState.Approved, "system:review", "done"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
    }

    [Fact]
    public async Task A_service_cannot_approve_and_nothing_is_written()
    {
        _store.FindItemAsync(7, Ct).Returns(Item(7, TicketState.Proposed));

        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, TicketState.Approved, "system:nightly", "auto"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        await _store.DidNotReceive().MoveItemAsync(Arg.Any<ItemMove>(), Ct);
    }

    [Fact]
    public async Task An_illegal_move_a_missing_ticket_and_a_rejected_candidate_are_refused()
    {
        _store.FindItemAsync(1, Ct).Returns(Item(1, TicketState.Lapsed));
        _store.FindItemAsync(3, Ct).Returns(Item(3, TicketState.Proposed, TicketGroup.Rejected));

        var final = await Ticket().HandleAsync(new MoveTicketCommand(1, TicketState.Approved, "owner:me", "x"), Ct);
        var missing = await Ticket().HandleAsync(new MoveTicketCommand(2, TicketState.Approved, "owner:me", "x"), Ct);
        var candidate = await Ticket().HandleAsync(new MoveTicketCommand(3, TicketState.Approved, "owner:me", "x"), Ct);

        final.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        missing.Error.Code.ShouldBe(PlanErrors.ItemNotFoundCode);
        candidate.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        await _store.DidNotReceive().MoveItemAsync(Arg.Any<ItemMove>(), Ct);
    }

    [Theory]
    [InlineData(TicketState.Modified, null)]
    [InlineData(TicketState.Approved, "edit")]
    public async Task Modifications_go_with_modified_and_only_with_it(TicketState to, string? modifications)
    {
        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, to, "owner:me", "x", modifications), Ct);

        result.Error.Code.ShouldBe(PlanErrors.InvalidCode);
    }

    [Fact]
    public async Task A_move_that_lost_the_race_is_stale()
    {
        _store.FindItemAsync(7, Ct).Returns(Item(7, TicketState.Proposed));
        _store.MoveItemAsync(Arg.Any<ItemMove>(), Ct).Returns(MoveStatus.Stale);

        var result = await Ticket().HandleAsync(new MoveTicketCommand(7, TicketState.Rejected, "owner:me", "no"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.StaleCode);
    }

    [Fact]
    public async Task A_plan_cannot_be_approved_while_a_ticket_is_open()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.InReview));
        _store.ItemsAsync(1, Ct).Returns([Item(1, TicketState.Approved), Item(2, TicketState.Proposed)]);

        var result = await PlanMove().HandleAsync(new MovePlanCommand(1, PlanState.Approved, "system:review", "done"), Ct);

        result.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        await _store.DidNotReceive().MovePlanAsync(Arg.Any<PlanMove>(), Ct);
    }

    [Fact]
    public async Task The_cutoff_lapses_the_undecided_tickets_and_the_plan_lapses_when_nothing_is_approved()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.Published));
        _store.ItemsAsync(1, Ct).Returns(
            [Item(1, TicketState.Proposed), Item(2, TicketState.Proposed, TicketGroup.Reserve)],
            [Item(1, TicketState.Lapsed), Item(2, TicketState.Lapsed, TicketGroup.Reserve)]);
        _store.MoveItemAsync(Arg.Any<ItemMove>(), Ct).Returns(MoveStatus.Created);
        _store.MovePlanAsync(Arg.Any<PlanMove>(), Ct).Returns(MoveStatus.Created);

        var result = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "system:cutoff"), Ct);

        result.Value.ShouldBe(new PlanMoveOutcome(1, "lapsed", 2));
        await _store.Received(2).MoveItemAsync(Arg.Is<ItemMove>(m => m.To == TicketState.Lapsed && m.Approval == null), Ct);
        await _store.Received(1).MovePlanAsync(Arg.Is<PlanMove>(m => m.To == PlanState.Lapsed), Ct);
    }

    [Fact]
    public async Task The_cutoff_keeps_what_the_owner_approved_and_never_approves_the_rest()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.InReview));
        _store.ItemsAsync(1, Ct).Returns(
            [Item(1, TicketState.Approved), Item(2, TicketState.Proposed)],
            [Item(1, TicketState.Approved), Item(2, TicketState.Lapsed)]);
        _store.MoveItemAsync(Arg.Any<ItemMove>(), Ct).Returns(MoveStatus.Created);
        _store.MovePlanAsync(Arg.Any<PlanMove>(), Ct).Returns(MoveStatus.Created);

        var result = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "system:cutoff"), Ct);

        result.Value.ShouldBe(new PlanMoveOutcome(1, "approved", 1));
        await _store.DidNotReceive().MoveItemAsync(Arg.Is<ItemMove>(m => m.To == TicketState.Approved || m.To == TicketState.Armed), Ct);
    }

    [Fact]
    public async Task The_cutoff_is_refused_a_second_before_it_and_at_it_it_runs()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.Published));
        _store.ItemsAsync(1, Ct).Returns([], []);
        _store.MovePlanAsync(Arg.Any<PlanMove>(), Ct).Returns(MoveStatus.Created);
        _time = new FakeTimeProvider(Cutoff.AddSeconds(-1));

        var before = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "system:cutoff"), Ct);
        _time.Advance(TimeSpan.FromSeconds(1));
        var at = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "system:cutoff"), Ct);

        before.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        at.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_cutoff_is_not_the_owners_act_and_not_for_a_plan_past_review()
    {
        _store.FindAsync(1, Ct).Returns(Plan(PlanState.Armed));

        var owner = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "owner:me"), Ct);
        var armed = await Cutoffs().HandleAsync(new ApplyReviewCutoffCommand(1, "system:cutoff"), Ct);

        owner.Error.Code.ShouldBe(PlanErrors.InvalidCode);
        armed.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
    }

    [Fact]
    public async Task Listing_refuses_a_bad_limit_and_getting_a_missing_plan_is_not_found()
    {
        var list = await new ListPlansHandler(_store).HandleAsync(new ListPlansQuery(null, null, 0), Ct);
        var get = await new GetPlanHandler(_store).HandleAsync(new GetPlanQuery(9), Ct);

        list.Error.Code.ShouldBe(PlanErrors.InvalidCode);
        get.Error.Code.ShouldBe(PlanErrors.NotFoundCode);
    }
}

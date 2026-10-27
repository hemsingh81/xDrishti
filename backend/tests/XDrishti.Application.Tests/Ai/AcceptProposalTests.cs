#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai.Proposals;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-08 AC 9, 10, 12: accept is the owner's act (capability, fresh password at the endpoint, checks again), writes a PROPOSED hypothesis or changes a basket and nothing else; reject starts the repeat block; neither repeats.</summary>
public sealed class AcceptProposalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task NothingAppliedAsync(ProposalRig rig)
    {
        await rig.Hypotheses.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Add.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Remove.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.LastDecision.ShouldBeNull();
    }

    [Fact]
    public async Task Accepting_a_hypothesis_writes_a_proposed_one_with_source_assistant_and_the_proposals_knob()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        var result = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("accepted");
        result.Value.Decision!.ResultRef.ShouldBe("hypothesis:77");
        result.Value.Decision.Auto.ShouldBeFalse();
        var call = rig.Hypotheses.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<ProposeHypothesisCommand>();
        call.Source.ShouldBe(HypothesisSource.Assistant);
        call.CellKey.ShouldBe(ProposalRig.Cell);
        call.BaselineRunKey.ShouldBe(ProposalRig.RunKey);
        call.Knob.ShouldBe(new ExperimentKnob("rr", ExperimentKnobKind.Parameter, "2", "2.5"));
        call.Rationale.ShouldStartWith($"Assistant proposal #{id}: ");
        call.Rationale.Length.ShouldBeLessThanOrEqualTo(500);
        call.Actor.ShouldBe(ProposalRig.Owner);
        rig.Store.LastDecision!.Actor.ShouldBe(ProposalRig.Owner);
        await rig.Add.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Accepting_a_basket_add_uses_the_owners_own_add_command_for_a_tracked_instrument()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());

        var result = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        result.Value.Decision!.ResultRef.ShouldBe($"basket:{ProposalRig.BasketId:D}");
        var call = rig.Add.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<AddBasketMembersCommand>();
        call.BasketId.ShouldBe(ProposalRig.BasketId);
        call.Input.InstrumentIds.ShouldBe([ProposalRig.EvbId]);
        call.Actor.ShouldBe(ProposalRig.Owner);
        await rig.Hypotheses.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Remove.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Accepting_a_basket_remove_removes_the_member()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.BasketItem("basket_remove", "TCS", reason: "TCS has 30 trades and a lower bound of 0.02."));

        var result = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        result.Value.Effect.ShouldBe("Removes TCS from basket EvalAlpha.");
        rig.Remove.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<RemoveBasketMembersCommand>().Input.InstrumentIds.ShouldBe([ProposalRig.TcsId]);
    }

    [Fact]
    public async Task A_second_accept_returns_the_first_result_and_changes_nothing()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        var first = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        var second = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        second.Value.Decision.ShouldBe(first.Value.Decision);
        rig.Hypotheses.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Without_the_capability_or_for_a_non_owner_nothing_is_applied()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Settings = rig.Settings with { AllowProposalAccept = false };

        var off = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);
        rig.Settings = rig.Settings with { AllowProposalAccept = true };
        var system = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, "system:auto-accept"), Ct);
        var empty = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, "owner:"), Ct);
        var cli = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, "cli:xd"), Ct);

        off.Error.Code.ShouldBe("assistant.capability_off");
        system.Error.Code.ShouldBe("assistant.owner_only");
        empty.Error.Code.ShouldBe("assistant.owner_only");
        cli.Error.Code.ShouldBe("assistant.owner_only");
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task The_capability_is_read_again_under_the_lock()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Store.OnLock = () => rig.Settings = rig.Settings with { AllowProposalAccept = false };

        (await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.capability_off");
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task A_missing_proposal_is_not_found()
    {
        using var rig = new ProposalRig();

        (await rig.Accepter().HandleAsync(new AcceptProposalCommand(99, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.proposal_not_found");
    }

    [Fact]
    public async Task A_rejected_or_expired_proposal_is_closed()
    {
        using var rig = new ProposalRig();
        var rejected = await rig.StoreAsync(ProposalRig.Hypothesis());
        var expiring = await rig.StoreAsync(ProposalRig.BasketItem());
        await rig.Rejecter().HandleAsync(new RejectProposalCommand(rejected, null, ProposalRig.Owner), Ct);
        rig.Time.Advance(TimeSpan.FromDays(4));

        var a = await rig.Accepter().HandleAsync(new AcceptProposalCommand(rejected, ProposalRig.Owner), Ct);
        var b = await rig.Accepter().HandleAsync(new AcceptProposalCommand(expiring, ProposalRig.Owner), Ct);

        a.Error.Code.ShouldBe("assistant.proposal_closed");
        b.Error.Code.ShouldBe("assistant.proposal_closed");
        await rig.Add.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task When_the_world_changed_the_checks_fail_again_and_the_accept_is_stale()
    {
        using var rig = new ProposalRig();
        var hypothesis = await rig.StoreAsync(ProposalRig.Hypothesis());
        var basket = await rig.StoreAsync(ProposalRig.BasketItem());
        rig.Facts.Cells[0] = rig.Facts.Cells[0] with { N = 10, Enough = false };
        rig.Facts.Instruments["EVB"] = new InstrumentFact(ProposalRig.EvbId, "EVB", false, false);

        var a = await rig.Accepter().HandleAsync(new AcceptProposalCommand(hypothesis, ProposalRig.Owner), Ct);
        var b = await rig.Accepter().HandleAsync(new AcceptProposalCommand(basket, ProposalRig.Owner), Ct);

        a.Error.Code.ShouldBe("assistant.proposal_stale");
        a.Error.Message.ShouldContain("evidence_insufficient");
        b.Error.Code.ShouldBe("assistant.proposal_stale");
        b.Error.Message.ShouldContain("instrument_tracked");
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task A_report_engine_that_cannot_answer_now_makes_a_basket_accept_stale_never_a_pass()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());
        rig.Facts.ReportsDown = true;

        (await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.proposal_stale");
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task Three_open_hypotheses_make_a_hypothesis_accept_stale()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Facts.Open.AddRange([new OpenHypothesisFact(1, "proposed", "a|1m|BUY|swing|X|*", "k"), new OpenHypothesisFact(2, "running", "b|1m|BUY|swing|X|*", "k"), new OpenHypothesisFact(3, "judged", "c|1m|BUY|swing|X|*", "k")]);

        (await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.proposal_stale");
    }

    [Fact]
    public async Task A_refusal_of_the_hypothesis_command_passes_through_and_writes_no_decision()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Hypotheses.HandleAsync(Arg.Any<ProposeHypothesisCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<HypothesisMoveOutcome>(HypothesisErrors.OpenLimit(["full"]))));

        var result = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);

        result.Error.Code.ShouldBe("hypotheses.open_limit");
        rig.Store.LastDecision.ShouldBeNull();
        rig.Store.Proposals[id].Decision.ShouldBeNull();
    }

    [Fact]
    public async Task The_decision_write_is_retried_once_and_a_second_failure_is_thrown_after_the_change()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Store.FailInserts = 1;
        var ok = await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);
        var other = await rig.StoreAsync(ProposalRig.BasketItem());
        rig.Store.FailInserts = 2;

        ok.IsSuccess.ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(async () => await rig.Accepter().HandleAsync(new AcceptProposalCommand(other, ProposalRig.Owner), Ct));
        rig.Add.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_accepts_are_serialised_so_one_applies_and_the_other_sees_the_first()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct).AsTask()));

        results.All(r => r.IsSuccess).ShouldBeTrue();
        rig.Hypotheses.ReceivedCalls().Count().ShouldBe(1);
        rig.Store.Locks.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Reject_records_the_reason_and_starts_the_repeat_block_and_cannot_be_repeated()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        var first = await rig.Rejecter().HandleAsync(new RejectProposalCommand(id, "not now <b>", ProposalRig.Owner), Ct);
        var second = await rig.Rejecter().HandleAsync(new RejectProposalCommand(id, "again", ProposalRig.Owner), Ct);

        first.Value.State.ShouldBe("rejected");
        first.Value.Decision!.Reason!.ShouldNotContain("<");
        second.Value.Decision.ShouldBe(first.Value.Decision);
        rig.Store.InsertTries.ShouldBe(1);
        (await rig.Store.RejectedSinceAsync(rig.Store.Proposals[id].DedupeKey, rig.Time.GetUtcNow().AddDays(-1), Ct)).ShouldBeTrue();
        await rig.Hypotheses.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Reject_checks_the_actor_the_reason_and_the_state()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());

        (await rig.Rejecter().HandleAsync(new RejectProposalCommand(id, null, "cli:xd"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
        (await rig.Rejecter().HandleAsync(new RejectProposalCommand(id, new string('x', 301), ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await rig.Rejecter().HandleAsync(new RejectProposalCommand(99, null, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.proposal_not_found");
        await rig.Accepter().HandleAsync(new AcceptProposalCommand(id, ProposalRig.Owner), Ct);
        (await rig.Rejecter().HandleAsync(new RejectProposalCommand(id, null, ProposalRig.Owner), Ct)).Error.Code.ShouldBe("assistant.proposal_closed");
    }
}

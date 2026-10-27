using XDrishti.Application.Ai.Proposals;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-08 AC 8, 14 and Contract: reads work while generation is off; the state is computed on read and nothing is updated.</summary>
public sealed class ReadProposalHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_works_while_generation_is_off_and_shows_expired_by_the_clock()
    {
        using var rig = new ProposalRig();
        var basket = await rig.StoreAsync(ProposalRig.BasketItem());
        var hypothesis = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Options.Enabled = false;
        rig.Time.Advance(TimeSpan.FromDays(4));
        var list = new ListProposalsHandler(rig.Store, rig.Time);

        var all = (await list.HandleAsync(new ListProposalsQuery(null, null), Ct)).Value;
        var pending = (await list.HandleAsync(new ListProposalsQuery("pending", null), Ct)).Value;
        var expired = (await list.HandleAsync(new ListProposalsQuery("expired", null), Ct)).Value;

        all.Select(p => p.Id).ShouldBe([hypothesis, basket]);
        pending.Select(p => p.Id).ShouldBe([hypothesis]);
        expired.Select(p => p.Id).ShouldBe([basket]);
        rig.Store.Proposals[basket].Decision.ShouldBeNull();
    }

    [Fact]
    public async Task The_view_carries_the_engines_evidence_checks_effect_and_notes()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        var view = (await new GetProposalHandler(rig.Store, rig.Time).HandleAsync(new GetProposalQuery(id), Ct)).Value;

        view.Kind.ShouldBe("hypothesis");
        view.State.ShouldBe("pending");
        view.Payload.Knob!.CandidateValue.ShouldBe("2.5");
        view.Evidence.Figures.ShouldNotBeEmpty();
        view.Checks.ShouldAllBe(c => c.Ok);
        view.Effect.ShouldBe($"Writes hypothesis {ProposalRig.Cell}: rr 2 to 2.5 as proposed; it will not run or change anything until you approve it.");
        view.Notes.ShouldContain("Each judged hypothesis counts as a trial and raises the bar for the next.");
        view.Decision.ShouldBeNull();
    }

    [Fact]
    public async Task A_basket_effect_is_built_from_the_engines_subject()
    {
        using var rig = new ProposalRig();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());

        (await new GetProposalHandler(rig.Store, rig.Time).HandleAsync(new GetProposalQuery(id), Ct)).Value.Effect.ShouldBe("Adds EVB to basket EvalAlpha.");
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(101, null, null)]
    [InlineData(10, "bogus", null)]
    [InlineData(10, null, "bogus")]
    public async Task A_bad_limit_state_or_kind_is_invalid(int limit, string? state, string? kind)
    {
        using var rig = new ProposalRig();

        (await new ListProposalsHandler(rig.Store, rig.Time).HandleAsync(new ListProposalsQuery(state, kind, limit), Ct)).Error.Code.ShouldBe("assistant.invalid");
    }

    [Fact]
    public async Task A_missing_proposal_is_not_found()
    {
        using var rig = new ProposalRig();

        (await new GetProposalHandler(rig.Store, rig.Time).HandleAsync(new GetProposalQuery(5), Ct)).Error.Code.ShouldBe("assistant.proposal_not_found");
    }

    [Fact]
    public async Task The_accept_switch_query_follows_the_setting()
    {
        using var rig = new ProposalRig();
        var handler = new ProposalAcceptAllowedHandler(rig.SettingsStore());

        (await handler.HandleAsync(new ProposalAcceptAllowedQuery(), Ct)).ShouldBeTrue();
        rig.Settings = rig.Settings with { AllowProposalAccept = false };
        (await handler.HandleAsync(new ProposalAcceptAllowedQuery(), Ct)).ShouldBeFalse();
    }
}

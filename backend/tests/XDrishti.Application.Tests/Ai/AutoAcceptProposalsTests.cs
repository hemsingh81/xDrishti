#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai.Proposals;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-08 AC 10a, D8 (the owner's rule that anything with a manual approval can also run automatically, default OFF): hypotheses only, every check again, 3 per India date, recorded as auto with the settings snapshot; unavailable never passes.</summary>
public sealed class AutoAcceptProposalsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProposalRig On()
    {
        var rig = new ProposalRig();
        rig.Settings = rig.Settings with { AutoAcceptHypothesis = true };
        return rig;
    }

    private static async Task<AutoAcceptOutcome> AskAsync(ProposalRig rig, params long[] ids) => (await rig.AutoHandler().HandleAsync(new AutoAcceptProposalsCommand(ids), Ct)).Value;

    private static async Task NothingAppliedAsync(ProposalRig rig)
    {
        await rig.Hypotheses.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.LastDecision.ShouldBeNull();
    }

    [Fact]
    public async Task By_default_the_option_is_off_and_nothing_is_written()
    {
        using var rig = On();
        rig.Settings = rig.Settings with { AutoAcceptHypothesis = false };
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        (await AskAsync(rig, id)).ShouldBe(new AutoAcceptOutcome(0, "off"));
        await NothingAppliedAsync(rig);
    }

    [Theory]
    [InlineData("proposals")]
    [InlineData("assistant")]
    [InlineData("nosnapshot")]
    public async Task Each_master_switch_and_the_setting_row_are_needed(string which)
    {
        using var rig = On();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        switch (which)
        {
            case "proposals":
                rig.Options.Enabled = false;
                break;
            case "assistant":
                rig.Settings = rig.Settings with { Enabled = false };
                break;
            default:
                rig.Settings = rig.Settings with { SnapshotHash = null };
                break;
        }

        (await AskAsync(rig, id)).Accepted.ShouldBe(0);
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task A_clean_hypothesis_is_accepted_as_auto_with_the_snapshot_and_written_proposed_only()
    {
        using var rig = On();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());

        var outcome = await AskAsync(rig, id);

        outcome.Accepted.ShouldBe(1);
        var decision = rig.Store.LastDecision!;
        (decision.Actor, decision.Auto, decision.SettingsId, decision.SnapshotHash, decision.Decision).ShouldBe(("system:auto-accept", true, 5L, new string('e', 64), "accepted"));
        decision.ResultRef.ShouldBe("hypothesis:77");
        var call = rig.Hypotheses.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<ProposeHypothesisCommand>();
        call.Actor.ShouldBe("system:auto-accept");
        call.Source.ShouldBe(XDrishti.Domain.Learning.HypothesisSource.Assistant);
        rig.Store.Proposals[id].Decision!.Auto.ShouldBeTrue();
    }

    [Fact]
    public async Task The_switch_is_read_again_under_the_lock_so_an_owner_who_switched_it_off_while_it_waited_gets_no_accept()
    {
        using var rig = On();
        var id = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Store.OnLock = () => rig.Settings = rig.Settings with { AutoAcceptHypothesis = false };

        (await AskAsync(rig, id)).Accepted.ShouldBe(0);
        await NothingAppliedAsync(rig);
    }

    [Fact]
    public async Task Only_hypotheses_are_ever_auto_accepted()
    {
        using var rig = On();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());

        (await AskAsync(rig, id)).LeftReason.ShouldBe("not_a_hypothesis");
        await rig.Add.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.LastDecision.ShouldBeNull();
    }

    [Fact]
    public async Task A_check_that_fails_now_an_unreadable_input_or_a_closed_proposal_leaves_it_to_the_owner()
    {
        using var rig = On();
        var thin = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Facts.Cells[0] = rig.Facts.Cells[0] with { N = 10, Enough = false };

        (await AskAsync(rig, thin)).LeftReason.ShouldBe("checks_not_passed");

        rig.Facts.Cells[0] = rig.Facts.Cells[0] with { N = 120, Enough = true };
        await rig.Rejecter().HandleAsync(new RejectProposalCommand(thin, null, ProposalRig.Owner), Ct);
        (await AskAsync(rig, thin)).LeftReason.ShouldBe("closed");
        await rig.Hypotheses.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.Proposals[thin].Decision!.Auto.ShouldBeFalse();
    }

    [Fact]
    public async Task At_most_three_per_india_date_are_auto_accepted_and_the_count_restarts_the_next_day()
    {
        using var rig = On();
        rig.Options.MaxPendingHypotheses = 10;
        rig.Facts.Runs[ProposalRig.RunKey] = new RunFact(ProposalRig.RunKey, "ORB", new RunDefinition("ORB", new Dictionary<string, double> { ["rr"] = 2, ["a"] = 2, ["b"] = 2, ["c"] = 2, ["d"] = 2 }, "{}", "buy={};sell={};", "{}"));
        var ids = new List<long>();
        foreach (var knob in new[] { "a", "b", "c", "d" })
        {
            ids.Add(await rig.StoreAsync(ProposalRig.Hypothesis().Replace("\"name\":\"rr\"", $"\"name\":\"{knob}\"", StringComparison.Ordinal)));
        }

        var outcome = await AskAsync(rig, [.. ids]);

        outcome.Accepted.ShouldBe(3);
        outcome.LeftReason.ShouldBe("limit_day");
        rig.Store.Proposals[ids[3]].Decision.ShouldBeNull();
        rig.Time.Advance(TimeSpan.FromDays(1));
        (await AskAsync(rig, ids[3])).Accepted.ShouldBe(1);
    }

    [Fact]
    public async Task An_unavailable_input_is_not_a_pass()
    {
        using var rig = On();
        var id = await rig.StoreAsync(ProposalRig.BasketItem());
        var hypothesis = await rig.StoreAsync(ProposalRig.Hypothesis());
        rig.Facts.Cells.Clear();

        (await AskAsync(rig, hypothesis)).LeftReason.ShouldBe("checks_not_passed");
        id.ShouldBeGreaterThan(0);
        await NothingAppliedAsync(rig);
    }
}

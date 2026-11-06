#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1, D7, AC11, AC12 (ADR 0220): a challenger is promoted only when it has 30 graded candidates over 4 weeks, the paired bootstrap against the champion gives 0.95, the P6-12 counterfactual is not FrozenBetter and the champion rule has nothing against it; an
/// unavailable figure is never a pass (so with the only evidence source there is, none, every promotion is refused with the reason); the owner promotes after a fresh password (the endpoint) and is recorded as the owner; the automatic actor needs <c>ml.auto_promote</c> ON and the
/// predicate without the switch, is recorded as <c>auto</c> with the switch row and never as an owner, and never re-promotes. Promotion never turns <c>ml.influence.enabled</c> on.
/// </summary>
public sealed class PromoteChallengerHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Owner = "owner:me@x.test";

    private static ChallengerFigures Good() => new(30, 4, 0.95, CounterfactualOutcome.NoDifference);

    private static MlRollbackFixture WithChallenger(ChallengerFigures? figures = null)
    {
        var f = new MlRollbackFixture { Figures = figures ?? ChallengerFigures.None };
        f.Add(7, ModelRole.Challenger);
        return f;
    }

    private static Task<XDrishti.Domain.Common.Result<PromotionOutcome>> PromoteAsync(MlRollbackFixture f, string actor = Owner, long id = 7) =>
        f.Promote.HandleAsync(new PromoteChallengerCommand(id, actor, "the evidence is in"), Ct).AsTask();

    [Fact]
    public async Task With_no_source_of_evidence_the_promotion_is_refused_with_every_reason_and_nothing_is_written()
    {
        var f = WithChallenger();

        var result = await PromoteAsync(f);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        result.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.Conflict);
        result.Error.Message.ShouldContain("not available");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        f.Registry.Designations.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_every_term_holds_the_owner_promotes_and_the_changelog_names_the_owner()
    {
        var f = WithChallenger(Good());
        f.Add(6, ModelRole.Champion, 'a');

        var result = await PromoteAsync(f);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        (result.Value.Changed, result.Value.Kind, result.Value.Role).ShouldBe((true, "promotion", "champion"));
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        f.Registry.Models[6].Role.ShouldBe(ModelRole.Retired);
        var row = f.Log.Rows.Single(r => r.Entry.Subject == "model:7" && r.Entry.ToState == "champion").Entry;
        (row.Actor, row.Reason, row.BeforeRef).ShouldBe((Owner, "the evidence is in", "model:6"));
    }

    [Theory]
    [InlineData(29, 4.0, 0.95, "need 30")]
    [InlineData(30, 3.9, 0.95, "need 4 weeks")]
    [InlineData(30, 4.0, 0.9499, "at least 0.95")]
    public async Task The_spec_hand_cases_29_candidates_3_weeks_and_a_probability_just_under_the_bar_are_refused(int candidates, double weeks, double probability, string reason)
    {
        var f = WithChallenger(new ChallengerFigures(candidates, weeks, probability, CounterfactualOutcome.RetrainingHelps));

        var result = await PromoteAsync(f);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        result.Error.Message.ShouldContain(reason);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Theory]
    [InlineData(CounterfactualOutcome.FrozenBetter)]
    [InlineData(CounterfactualOutcome.Inconclusive)]
    public async Task A_counterfactual_that_says_the_frozen_model_is_better_or_says_nothing_is_not_a_pass(CounterfactualOutcome outcome)
    {
        var f = WithChallenger(new ChallengerFigures(30, 4, 0.99, outcome));

        var result = await PromoteAsync(f);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task The_champion_rule_still_applies_a_model_that_may_not_be_champion_is_refused_even_with_good_figures()
    {
        var f = new MlRollbackFixture { Figures = Good() };
        var weak = MlInfluenceKit.Model(7, ModelRole.Challenger, heldOut: false);
        f.Registry.Models[7] = weak;

        var result = await PromoteAsync(f);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        result.Error.Message.ShouldContain("No hold-out was kept");
    }

    [Fact]
    public async Task Promotion_never_turns_the_influence_switch_on_and_promoting_a_champion_again_is_a_noop()
    {
        var f = WithChallenger(Good());

        var first = await PromoteAsync(f);
        var second = await PromoteAsync(f);

        first.Value.Changed.ShouldBeTrue();
        second.Value.Kind.ShouldBe("already_champion");
        second.Value.Changed.ShouldBeFalse();
        f.Switches.ShouldBe(MlSwitches.AllOff, "the handler holds no switch store (an architecture test pins that)");
        f.Registry.Designations.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("auto-ish")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_an_owner_or_auto_may_promote(string actor)
    {
        var f = WithChallenger(Good());

        var result = await PromoteAsync(f, actor);

        result.Error.Code.ShouldBe(MlSafetyErrors.OwnerOnlyCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task An_unknown_model_is_not_found()
    {
        var f = WithChallenger(Good());

        (await PromoteAsync(f, id: 99)).Error.Code.ShouldBe(ModelErrors.NotFoundCode);
    }

    // ---- the automatic option ----

    [Fact]
    public async Task The_automatic_actor_is_refused_while_the_setting_is_off_the_default()
    {
        var f = WithChallenger(Good());
        await f.MakePredicateHoldAsync(f.Registry.Models[7]);

        var result = await PromoteAsync(f, MlActors.Auto);

        result.Error.Code.ShouldBe(MlSafetyErrors.AutoPromoteOffCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task With_the_setting_on_and_both_the_rule_and_the_predicate_without_the_switch_holding_it_promotes_as_auto_with_the_switch_row_and_never_as_an_owner()
    {
        var f = WithChallenger(Good());
        await f.MakePredicateHoldAsync(f.Registry.Models[7]);
        f.Switches = MlRollbackFixture.On(promote: true);

        var result = await PromoteAsync(f, MlActors.Auto);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        var row = f.Log.Rows.Single(r => r.Entry.Subject == "model:7" && r.Entry.ToState == "champion").Entry;
        row.Actor.ShouldBe("auto");
        row.EvidenceIds.Take(2).ShouldBe(["model:7", "ml-switch:4"]);
        row.EvidenceIds[2].ShouldStartWith("settings:");
        row.Reason.ShouldContain("automatic promotion");
        f.Log.Rows.Any(r => r.Entry.Actor.StartsWith("owner:", StringComparison.Ordinal)).ShouldBeFalse();
        f.Switches.Influence.On.ShouldBeFalse("promotion never turns ml.influence.enabled on");
    }

    [Fact]
    public async Task With_the_setting_on_but_no_predicate_evidence_the_good_promotion_figures_are_not_enough_for_auto()
    {
        var f = WithChallenger(Good());
        f.Switches = MlRollbackFixture.On(promote: true);

        var result = await PromoteAsync(f, MlActors.Auto);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        result.Error.Message.ShouldContain("not been scored on the locked hold-out");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task With_the_setting_on_but_no_promotion_evidence_the_predicate_alone_is_not_enough_for_auto_which_is_how_it_stands_today()
    {
        var f = WithChallenger();
        await f.MakePredicateHoldAsync(f.Registry.Models[7]);
        f.Switches = MlRollbackFixture.On(promote: true);

        var result = await PromoteAsync(f, MlActors.Auto);

        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task The_automatic_actor_never_re_promotes_a_retired_model_or_a_model_that_was_proposed_for_rollback()
    {
        var f = WithChallenger(Good());
        f.Add(8, ModelRole.Retired, 'c');
        await f.MakePredicateHoldAsync(f.Registry.Models[7]);
        f.Switches = MlRollbackFixture.On(promote: true);

        var retired = await PromoteAsync(f, MlActors.Auto, 8);
        f.Store.Rows.Add(new XDrishti.Application.Abstractions.Learning.RollbackProposalRow(1, new XDrishti.Application.Abstractions.Learning.NewRollbackProposal(7, "calibration", 1, 1, "t", [], null, false, new string('a', 64)), new DateOnly(2026, 11, 1), f.Time.GetUtcNow(), "proposed", null, null, null));
        var rolledBack = await PromoteAsync(f, MlActors.Auto);

        retired.Error.Code.ShouldBe(MlSafetyErrors.OwnerOnlyCode);
        rolledBack.Error.Code.ShouldBe(MlSafetyErrors.OwnerOnlyCode);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Retired);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    // ---- re-promotion ----

    [Fact]
    public async Task The_owner_re_promotes_a_retired_model_only_if_its_latest_evaluation_satisfies_the_predicate_without_the_switch()
    {
        var f = new MlRollbackFixture();
        var retired = f.Add(8, ModelRole.Retired, 'c');

        var refused = await PromoteAsync(f, id: 8);
        await f.MakePredicateHoldAsync(retired);
        var promoted = await PromoteAsync(f, id: 8);

        refused.Error.Code.ShouldBe(MlSafetyErrors.RepromotionRefusedCode);
        promoted.IsSuccess.ShouldBeTrue(promoted.IsFailure ? promoted.Error.Message : string.Empty);
        (promoted.Value.Kind, promoted.Value.Changed).ShouldBe(("re_promotion", true));
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Champion);
    }
}

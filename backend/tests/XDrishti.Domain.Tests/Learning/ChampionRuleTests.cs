using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The champion rule and the role codes (spec P6-07).</summary>
public sealed class ChampionRuleTests
{
    private static readonly DateOnly Held = new(2026, 7, 1);

    [Theory]
    [InlineData(ValidationOutcome.Eligible, true)]
    [InlineData(ValidationOutcome.Inconclusive, false)]
    [InlineData(ValidationOutcome.CannotBeatBaselines, false)]
    public void Only_an_eligible_verdict_may_be_champion(ValidationOutcome verdict, bool expected)
    {
        ChampionRule.MayBeChampion(verdict, Held, 6, 1, 1).ShouldBe(expected);
        ChampionRule.Refusals(ModelRole.Champion, verdict, Held, 6, 1, 1).Count.ShouldBe(expected ? 0 : 1);
    }

    [Fact]
    public void A_model_without_a_kept_hold_out_may_not_be_champion()
    {
        ChampionRule.Reasons(ValidationOutcome.Eligible, null, 6, 1, 1).ShouldHaveSingleItem().ShouldContain("hold-out");
    }

    [Fact]
    public void A_model_judged_on_too_few_folds_may_not_be_champion()
    {
        ChampionRule.MayBeChampion(ValidationOutcome.Eligible, Held, ValidationOptions.DefaultMinFolds - 1, 1, 1).ShouldBeFalse();
        ChampionRule.Reasons(ValidationOutcome.Eligible, Held, 3, 1, 1).ShouldHaveSingleItem().ShouldContain("3 folds");
    }

    [Fact]
    public void A_model_judged_with_fewer_trials_than_the_registry_shows_may_not_be_champion()
    {
        ChampionRule.MayBeChampion(ValidationOutcome.Eligible, Held, 6, 2, 3).ShouldBeFalse();
        ChampionRule.Reasons(ValidationOutcome.Eligible, Held, 6, 2, 3).ShouldHaveSingleItem().ShouldContain("3 models");
        ChampionRule.MayBeChampion(ValidationOutcome.Eligible, Held, 6, 3, 3).ShouldBeTrue();
    }

    [Fact]
    public void Every_failed_condition_is_listed()
    {
        ChampionRule.Reasons(ValidationOutcome.Inconclusive, null, 2, 1, 4).Count.ShouldBe(4);
    }

    [Theory]
    [InlineData(ModelRole.Challenger)]
    [InlineData(ModelRole.Retired)]
    public void Any_record_may_be_challenger_or_retired(ModelRole role)
    {
        foreach (var verdict in Enum.GetValues<ValidationOutcome>())
        {
            ChampionRule.Refusals(role, verdict, null, 0, 1, 9).ShouldBeEmpty();
        }
    }

    [Fact]
    public void Role_codes_round_trip_and_an_unknown_code_is_none()
    {
        foreach (var role in Enum.GetValues<ModelRole>())
        {
            ModelRoleExtensions.Parse(role.Code()).ShouldBe(role);
        }

        ModelRoleExtensions.Parse("king").ShouldBeNull();
        ModelRoleExtensions.Parse(null).ShouldBeNull();
        ModelRoleExtensions.Parse("Champion").ShouldBeNull("codes are lower case");
    }
}

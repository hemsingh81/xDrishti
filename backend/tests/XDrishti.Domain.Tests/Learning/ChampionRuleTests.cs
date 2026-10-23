using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The champion rule and the role codes (spec P6-07).</summary>
public sealed class ChampionRuleTests
{
    [Theory]
    [InlineData(ValidationOutcome.Eligible, true)]
    [InlineData(ValidationOutcome.Inconclusive, false)]
    [InlineData(ValidationOutcome.CannotBeatBaselines, false)]
    public void Only_an_eligible_verdict_may_be_champion(ValidationOutcome verdict, bool expected)
    {
        ChampionRule.MayBeChampion(verdict).ShouldBe(expected);
        ChampionRule.Allows(ModelRole.Champion, verdict).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ModelRole.Challenger)]
    [InlineData(ModelRole.Retired)]
    public void Any_verdict_may_be_challenger_or_retired(ModelRole role)
    {
        foreach (var verdict in Enum.GetValues<ValidationOutcome>())
        {
            ChampionRule.Allows(role, verdict).ShouldBeTrue();
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

using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-10 part 10c-2 (ADR 0217): the thresholds of the influence predicate.</summary>
public sealed class MlInfluenceOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_proposals_and_usable()
    {
        var o = new MlInfluenceOptions();

        (o.HoldOutMinRows, o.HoldOutMinProbability, o.ForwardMinCandidates, o.ForwardMinWeeks, o.ForwardMaxEcePp).ShouldBe((100, 0.95, 30, 4, 5.0));
        o.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_key_text_changes_with_a_value()
    {
        new MlInfluenceOptions().KeyText().ShouldNotBe(new MlInfluenceOptions(ForwardMinWeeks: 5).KeyText());
    }

    [Fact]
    public void Configuration_can_only_tighten_the_specs_proposals()
    {
        new MlInfluenceOptions(250, 0.975, 60, 8, 3).Problems().ShouldBeEmpty();
        new MlInfluenceOptions(100, 0.95, 30, 4, 5).Problems().ShouldBeEmpty();
    }

    public static TheoryData<MlInfluenceOptions> Bad() => new()
    {
        new MlInfluenceOptions(HoldOutMinRows: 99),
        new MlInfluenceOptions(HoldOutMinRows: 100001),
        new MlInfluenceOptions(HoldOutMinProbability: 0.949),
        new MlInfluenceOptions(HoldOutMinProbability: 1),
        new MlInfluenceOptions(HoldOutMinProbability: double.NaN),
        new MlInfluenceOptions(ForwardMinCandidates: 29),
        new MlInfluenceOptions(ForwardMinWeeks: 3),
        new MlInfluenceOptions(ForwardMinWeeks: 521),
        new MlInfluenceOptions(ForwardMaxEcePp: 0),
        new MlInfluenceOptions(ForwardMaxEcePp: 5.01),
        new MlInfluenceOptions(ForwardMaxEcePp: double.PositiveInfinity),
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public void A_value_out_of_range_is_a_problem(MlInfluenceOptions options)
    {
        options.Problems().ShouldNotBeEmpty();
    }
}

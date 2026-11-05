using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-10 part 10b (ADR 0215): the settings of the ladder and the stability checks.</summary>
public sealed class LadderOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_proposals_report_only_and_usable()
    {
        var o = new LadderOptions();

        o.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        (o.Epv, o.MaxLogisticFeatures, o.MinTreeEvents, o.MinProbability).ShouldBe((15, 8, 100, 0.95));
        (o.PsiWatch, o.PsiAlarm, o.PsiBins).ShouldBe((0.10, 0.25, 10));
        o.MaxFlipShare.ShouldBe(1.0 / 3.0);
        o.IsOn.ShouldBeTrue();
        o.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Off_computes_nothing_and_gate_is_refused()
    {
        new LadderOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
        new LadderOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
        new LadderOptions(ConfidenceMode.Gate).Problems().ShouldNotBeEmpty();
        new LadderOptions((ConfidenceMode)9).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_key_text_changes_with_a_value()
    {
        new LadderOptions().KeyText().ShouldNotBe(new LadderOptions(Epv: 10).KeyText());
    }

    public static TheoryData<LadderOptions> Bad() => new()
    {
        new LadderOptions(Epv: 4),
        new LadderOptions(Epv: 51),
        new LadderOptions(MaxLogisticFeatures: 0),
        new LadderOptions(MinTreeEvents: 9),
        new LadderOptions(MinProbability: 0.4),
        new LadderOptions(MinProbability: 1.0),
        new LadderOptions(MinProbability: double.NaN),
        new LadderOptions(PsiWatch: 0),
        new LadderOptions(PsiWatch: 0.3, PsiAlarm: 0.25),
        new LadderOptions(PsiAlarm: 6),
        new LadderOptions(PsiBins: 1),
        new LadderOptions(MinRows: 4),
        new LadderOptions(MaxFlipShare: 0),
        new LadderOptions(MaxFlipShare: 0.5),
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public void A_value_out_of_range_is_a_problem(LadderOptions options)
    {
        options.Problems().ShouldNotBeEmpty();
    }
}

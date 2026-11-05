using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-10 part 10c-1 (ADR 0216): the settings of the paired bootstrap.</summary>
public sealed class PairedBootstrapOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_proposals_report_only_and_usable()
    {
        var o = new PairedBootstrapOptions();

        o.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        (o.Draws, o.Level, o.MinDays, o.PromisingFrom).ShouldBe((2000, 0.90, 10, 0.90));
        o.IsOn.ShouldBeTrue();
        o.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Off_computes_nothing_and_gate_is_refused()
    {
        new PairedBootstrapOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
        new PairedBootstrapOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
        new PairedBootstrapOptions(ConfidenceMode.Gate).Problems().ShouldNotBeEmpty();
        new PairedBootstrapOptions((ConfidenceMode)9).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_key_text_changes_with_a_value()
    {
        new PairedBootstrapOptions().KeyText().ShouldNotBe(new PairedBootstrapOptions(Draws: 3000).KeyText());
    }

    public static TheoryData<PairedBootstrapOptions> Bad() => new()
    {
        new PairedBootstrapOptions(Draws: 99),
        new PairedBootstrapOptions(Draws: 20001),
        new PairedBootstrapOptions(Level: 0.5),
        new PairedBootstrapOptions(Level: 1.0),
        new PairedBootstrapOptions(Level: double.NaN),
        new PairedBootstrapOptions(MinDays: 1),
        new PairedBootstrapOptions(MinDays: 1001),
        new PairedBootstrapOptions(PromisingFrom: 0.49),
        new PairedBootstrapOptions(PromisingFrom: 0.995),
        new PairedBootstrapOptions(PromisingFrom: double.NaN),
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public void A_value_out_of_range_is_a_problem(PairedBootstrapOptions options)
    {
        options.Problems().ShouldNotBeEmpty();
    }
}

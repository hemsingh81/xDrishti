using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

public sealed class ConfidenceOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_and_report_only()
    {
        var o = new ConfidenceOptions();

        o.Problems().ShouldBeEmpty();
        o.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        (o.MinSessionsForAutocorr, o.MinTradesForEstimate, o.MinClusters, o.Alpha, o.Power, o.SigmaFloorR, o.Confidence, o.BenchmarkSr).ShouldBe((20, 5, 5, 0.05, 0.80, 0.5, 0.95, 0.0));
        o.EdgeOr(0.15).ShouldBe(0.15);
        new ConfidenceOptions(TargetEdgeR: 0.2).EdgeOr(0.15).ShouldBe(0.2);
    }

    [Theory]
    [MemberData(nameof(Bad))]
    public void A_value_out_of_range_is_refused(ConfidenceOptions o) => o.Problems().Count.ShouldBe(1);

    public static TheoryData<ConfidenceOptions> Bad() => new()
    {
        new ConfidenceOptions(MinSessionsForAutocorr: 4),
        new ConfidenceOptions(MinSessionsForAutocorr: 251),
        new ConfidenceOptions(MinTradesForEstimate: 2),
        new ConfidenceOptions(MinClusters: 51),
        new ConfidenceOptions(Alpha: 0.3),
        new ConfidenceOptions(Power: 0.4),
        new ConfidenceOptions(SigmaFloorR: 6),
        new ConfidenceOptions(TargetEdgeR: 0),
        new ConfidenceOptions(Confidence: 1),
        new ConfidenceOptions(BenchmarkSr: double.NaN),
        new ConfidenceOptions(Mode: (ConfidenceMode)9),
    };

    [Fact]
    public void The_key_text_changes_with_every_value_that_changes_a_stored_figure()
    {
        var a = new ConfidenceOptions().KeyText();

        new ConfidenceOptions(Mode: ConfidenceMode.Gate).KeyText().ShouldNotBe(a);
        new ConfidenceOptions(MinClusters: 6).KeyText().ShouldNotBe(a);
        new ConfidenceOptions(TargetEdgeR: 0.2).KeyText().ShouldNotBe(a);
        new ConfidenceOptions().KeyText().ShouldBe(a);
    }
}

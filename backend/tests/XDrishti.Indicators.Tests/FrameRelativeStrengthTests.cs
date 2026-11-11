using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Strength;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>BK-09 criteria 6 and 9 at the frame: the features read the stored row of the previous closed session (not the date, not two sessions back), are NaN when unknown, and take only the pre-registered parameters.</summary>
public sealed class FrameRelativeStrengthTests
{
    private static readonly DateOnly Mon = new(2026, 8, 3);

    private static readonly DateOnly Tue = new(2026, 8, 4);

    private static readonly DateOnly Wed = new(2026, 8, 5);

    private static readonly DateOnly Thu = new(2026, 8, 6);

    private static readonly FeatureSpec Mom = new("mom_rank", Timeframe.M1, PriceSource.Close, 2, 126, 21);

    private static RelativeStrengthDays Rows(params (DateOnly AsOf, double Value)[] rows) =>
        new(rows.Select(r => new RelativeStrengthPoint(DateFeatureKind.MomRank, 2, 126, 21, r.AsOf, r.Value)));

    private static double ReadOn(RelativeStrengthDays rows, DateOnly date)
    {
        var frame = FeatureFrame.Create([Mom], new FrameOptions(RelativeStrength: rows)).Value;
        frame.Step(Minutes.Close(date, Minutes.Open, 100));
        return frame[0];
    }

    [Fact]
    public void The_value_on_day_D_is_the_stored_row_of_the_previous_session_not_of_D_and_not_of_two_sessions_back()
    {
        var rows = Rows((Mon, 0.1), (Tue, 0.5), (Wed, 0.9));

        ReadOn(rows, Wed).ShouldBe(0.5, "as_of Tuesday is the last closed session on Wednesday: no second shift");
        ReadOn(rows, Tue).ShouldBe(0.1);
        double.IsNaN(ReadOn(rows, Mon)).ShouldBeTrue("nothing is stored before Monday");
    }

    [Fact]
    public void Removing_or_replacing_every_row_from_day_k_on_changes_nothing_read_up_to_day_k()
    {
        var full = Rows((Mon, 0.1), (Tue, 0.5), (Wed, 0.9), (Thu, 0.3));
        var poisoned = Rows((Mon, 0.1), (Tue, 0.5), (Wed, 123), (Thu, -5));
        var cut = Rows((Mon, 0.1), (Tue, 0.5));

        foreach (var day in new[] { Mon, Tue, Wed })
        {
            var expected = ReadOn(full, day);
            (double.IsNaN(expected) ? double.NaN : ReadOn(poisoned, day)).ShouldBe(expected);
            ReadOn(cut, day).ShouldBe(expected);
        }
    }

    [Fact]
    public void A_stored_state_that_is_not_a_value_reads_as_unknown_and_so_does_a_feature_with_no_series()
    {
        var rows = Rows((Mon, double.NaN));

        double.IsNaN(ReadOn(rows, Tue)).ShouldBeTrue();
        double.IsNaN(ReadOn(RelativeStrengthDays.Empty, Tue)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("mom_rank", 9, 126, 21, "'basket' must be one of 1, 2, 3, 4")]
    [InlineData("mom_rank", 2, 100, 21, "'lookback' must be one of 126, 252")]
    [InlineData("mom_rank", 2, 126, 5, "'skip' must be one of 21")]
    [InlineData("rs_vs_index", 10, 0, 0, "'n' must be one of 20")]
    [InlineData("sector_rs", 30, 0, 0, "'window' must be one of 21, 63, 126")]
    public void A_parameter_outside_the_pre_registered_set_is_refused_with_the_allowed_values(string name, double p1, double p2, double p3, string message)
    {
        var created = FeatureFrame.Create([new FeatureSpec(name, Timeframe.M1, PriceSource.Close, p1, p2, p3)], new FrameOptions(RelativeStrength: RelativeStrengthDays.Empty));

        created.IsFailure.ShouldBeTrue();
        created.Error.Fields.ShouldNotBeNull().Single().Message.ShouldContain(message);
    }

    [Fact]
    public void The_pre_registered_values_are_accepted_and_the_baskets_are_named()
    {
        foreach (var spec in new[] { Mom, Mom with { P1 = 4, P2 = 252 }, new FeatureSpec("rs_vs_index", Timeframe.M1, PriceSource.Close, 20), new FeatureSpec("sector_rs", Timeframe.M1, PriceSource.Close, 126) })
        {
            FeatureFrame.Create([spec], new FrameOptions(RelativeStrength: RelativeStrengthDays.Empty)).IsSuccess.ShouldBeTrue(spec.Name);
        }

        StrengthBaskets.TryCode("nifty 100", out var code).ShouldBeTrue();
        code.ShouldBe(2);
        StrengthBaskets.TryCode("Nifty 200", out _).ShouldBeFalse();
    }
}

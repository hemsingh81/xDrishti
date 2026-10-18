using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public class SlippageConfigTests
{
    [Fact]
    public void Default_is_the_doc_16_numbers()
    {
        var d = SlippageConfig.Default;

        d.Ticks.ShouldBe(1);
        d.BarRangeFrac.ShouldBe(0.05);
        d.StopExitExtraTicks.ShouldBe(2);
        d.GapFill.ShouldBe(GapFillRule.AtOpen);
        d.IsNone.ShouldBeFalse();
    }

    [Fact]
    public void None_is_zero_and_says_so()
    {
        var n = SlippageConfig.None;

        (n.Ticks, n.BarRangeFrac, n.StopExitExtraTicks).ShouldBe((0d, 0d, 0d));
        n.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Create_accepts_the_bounds()
    {
        var result = SlippageConfig.Create(100, 1, 100);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ticks.ShouldBe(100);
    }

    [Fact]
    public void Create_of_zeros_equals_None()
    {
        SlippageConfig.Create(0, 0, 0).Value.ShouldBe(SlippageConfig.None);
    }

    [Theory]
    [InlineData(double.NaN, 0.05, 2, "Ticks")]
    [InlineData(double.PositiveInfinity, 0.05, 2, "Ticks")]
    [InlineData(-1, 0.05, 2, "Ticks")]
    [InlineData(101, 0.05, 2, "Ticks")]
    [InlineData(1, double.NaN, 2, "BarRangeFrac")]
    [InlineData(1, -0.01, 2, "BarRangeFrac")]
    [InlineData(1, 1.01, 2, "BarRangeFrac")]
    [InlineData(1, double.NegativeInfinity, 2, "BarRangeFrac")]
    [InlineData(1, 0.05, double.NaN, "StopExitExtraTicks")]
    [InlineData(1, 0.05, -2, "StopExitExtraTicks")]
    [InlineData(1, 0.05, 100.5, "StopExitExtraTicks")]
    [InlineData(1, 0.05, double.PositiveInfinity, "StopExitExtraTicks")]
    public void Create_refuses_a_bad_number_and_names_the_field(double ticks, double frac, double extra, string field)
    {
        var result = SlippageConfig.Create(ticks, frac, extra);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.slippage_invalid");
        result.Error.Message.ShouldContain(field);
    }

    [Fact]
    public void Create_refuses_an_undefined_gap_rule()
    {
        var result = SlippageConfig.Create(1, 0.05, 2, (GapFillRule)7);

        result.Error.Code.ShouldBe("simulation.slippage_invalid");
        result.Error.Message.ShouldContain("GapFill");
    }

    [Fact]
    public void Only_Create_can_build_one_and_nothing_can_be_changed()
    {
        typeof(SlippageConfig).GetConstructors().ShouldBeEmpty();
        typeof(SlippageConfig).GetProperties().ShouldAllBe(p => p.SetMethod == null);
    }
}

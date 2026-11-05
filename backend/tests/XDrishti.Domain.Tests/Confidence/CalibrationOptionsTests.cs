using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-10 part 10a (ADR 0214): the settings of the calibration cut.</summary>
public sealed class CalibrationOptionsTests
{
    [Fact]
    public void The_default_is_the_specs_cut_of_500_rows_and_usable()
    {
        var o = new CalibrationOptions();

        o.IsotonicMinRows.ShouldBe(500);
        o.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100001)]
    public void A_cut_out_of_range_is_a_problem(int rows)
    {
        new CalibrationOptions(rows).Problems().ShouldNotBeEmpty();
    }
}

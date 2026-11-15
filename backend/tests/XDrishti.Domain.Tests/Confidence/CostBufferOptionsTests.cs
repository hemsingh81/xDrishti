using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>GROSS-01 (ADR 0294): the cost buffer's range, label and snapshot.</summary>
public sealed class CostBufferOptionsTests
{
    [Fact]
    public void The_default_is_a_tenth_of_r_and_usable()
    {
        var o = new CostBufferOptions();

        (o.BufferR, o.IsValid, o.Label(), o.KeyText()).ShouldBe((0.10, true, "gross_less_buffer(0.10R)", "Confidence:Costs:BufferR=0.1"));
    }

    [Theory]
    [InlineData(0.05, true)]
    [InlineData(0.50, true)]
    [InlineData(0.0499, false)]
    [InlineData(0.5001, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void Only_a_finite_buffer_from_five_hundredths_to_a_half_is_usable(double buffer, bool valid)
    {
        new CostBufferOptions(buffer).IsValid.ShouldBe(valid);
    }

    [Fact]
    public void A_trade_is_its_gross_r_less_the_buffer()
    {
        new CostBufferOptions(0.25).Apply(0.20).ShouldBe(-0.05, 1e-12);
    }
}

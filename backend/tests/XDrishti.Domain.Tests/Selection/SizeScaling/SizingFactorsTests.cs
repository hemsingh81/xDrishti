using XDrishti.Domain.Research;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Domain.Tests.Selection.SizeScaling;

/// <summary>BK-10 criteria 3, 4 and 7 (down only): the quantity by hand and the property that no path gives more than the fixed-risk quantity.</summary>
public sealed class SizingFactorsTests
{
    private static long Fixed() => Sizing.Quantity(1_000_000m, SizingProfile.Standard, 100m, 10m).Value;

    [Fact]
    public void Fixed_quantity_by_hand_is_five_hundred_and_the_volatility_scale_gives_380_with_risk_inside_the_budget()
    {
        var q = Fixed();

        q.ShouldBe(500);
        var scaled = SizingFactors.Quantity(q, SizingFactors.Combine(0.7619, 1.0));
        scaled.ShouldBe(380);
        (scaled * 10).ShouldBeLessThanOrEqualTo(5000);
    }

    [Fact]
    public void In_reduced_mode_the_smaller_of_the_two_factors_is_taken_once_giving_375_not_281()
    {
        var factor = SizingFactors.Combine(0.7619, 0.75);

        factor.ShouldBe(0.75);
        SizingFactors.Quantity(500, factor).ShouldBe(375);
    }

    [Fact]
    public void Without_a_volatility_scale_the_gate_scale_stands_and_nothing_is_a_guess()
    {
        SizingFactors.Combine(null, 0.75).ShouldBe(0.75);
        SizingFactors.Combine(null, 1.0).ShouldBe(1.0);
        SizingFactors.Combine(double.NaN, 1.0).ShouldBe(1.0);
    }

    [Fact]
    public void A_factor_above_one_is_never_honoured()
    {
        SizingFactors.Combine(5.0, 1.0).ShouldBe(1.0);
        SizingFactors.Combine(null, 3.0).ShouldBe(1.0);
        SizingFactors.Quantity(500, 4.0).ShouldBe(500);
    }

    [Fact]
    public void Capacity_cap_by_hand_at_the_one_capacity_share_cuts_380_to_100()
    {
        var cap = CapacityCap.Quantity(2_000_000, 100, 0.005);

        cap.ShouldBe(100); // floor(0.005 * 2,000,000 / 100)
        Math.Min(380, cap!.Value).ShouldBe(100);
        CapacityCap.Quantity(2_000_000, 100, 0.004).ShouldBe(80); // tighter is allowed
    }

    [Theory]
    [InlineData(null, 100.0, 0.005)]
    [InlineData(2_000_000.0, null, 0.005)]
    [InlineData(0.0, 100.0, 0.005)]
    [InlineData(2_000_000.0, 0.0, 0.005)]
    [InlineData(2_000_000.0, 100.0, 0.0)]
    [InlineData(2_000_000.0, 100.0, 0.5)]
    [InlineData(2_000_000.0, 100.0, 0.01)] // above the one capacity share: refused, never a looser cap
    [InlineData(double.NaN, 100.0, 0.005)]
    public void A_capacity_that_cannot_be_computed_is_unavailable_never_a_number(double? adv, double? entry, double pct)
    {
        CapacityCap.Quantity(adv, entry, pct).ShouldBeNull();
    }

    [Fact]
    public void Property_the_final_quantity_is_never_larger_than_the_fixed_one()
    {
        var random = new Random(7);
        for (var i = 0; i < 20_000; i++)
        {
            var fixedQuantity = random.NextInt64(0, 1_000_000);
            double? vol = random.Next(4) == 0 ? null : (random.NextDouble() * 4) - 1; // includes values above 1 and below 0
            var gate = random.NextDouble() * 2;
            var factor = SizingFactors.Combine(vol, gate);

            var quantity = SizingFactors.Quantity(fixedQuantity, factor);

            factor.ShouldBeGreaterThan(0);
            factor.ShouldBeLessThanOrEqualTo(1.0);
            quantity.ShouldBeLessThanOrEqualTo(fixedQuantity);
            quantity.ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Property_a_larger_volatility_scale_never_gives_a_smaller_quantity_and_a_smaller_one_never_a_larger()
    {
        var random = new Random(8);
        for (var i = 0; i < 5000; i++)
        {
            var q = random.NextInt64(1, 100_000);
            var a = random.NextDouble();
            var b = random.NextDouble();

            var low = SizingFactors.Quantity(q, SizingFactors.Combine(Math.Min(a, b), 1.0));
            var high = SizingFactors.Quantity(q, SizingFactors.Combine(Math.Max(a, b), 1.0));

            low.ShouldBeLessThanOrEqualTo(high);
        }
    }
}

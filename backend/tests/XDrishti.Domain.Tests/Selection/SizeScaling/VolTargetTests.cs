using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Domain.Tests.Selection.SizeScaling;

/// <summary>BK-10 criteria 1 and 2 by hand, and the properties of the volatility scale: always in [Floor, 1.0].</summary>
public sealed class VolTargetTests
{
    [Fact]
    public void Ewma_by_hand_two_steps_from_a_one_percent_day()
    {
        var variances = VolTarget.Evolve(0.0001, [0.02, 0.0], 0.94);

        variances[1].ShouldBe(0.000118, 1e-12);
        variances[2].ShouldBe(0.00011092, 1e-12);
        Math.Sqrt(variances[2]).ShouldBe(0.010531, 1e-6);
    }

    [Fact]
    public void Fewer_than_sixty_returns_is_insufficient_with_no_number_and_no_scale()
    {
        var reading = VolTarget.Read([.. Enumerable.Repeat(0.01, 59)], new SizingOptions());

        reading.Status.ShouldBe("insufficient_returns");
        reading.Returns.ShouldBe(59);
        reading.Sigma.ShouldBeNull();
        reading.Scale.ShouldBeNull();
    }

    [Fact]
    public void Sixty_returns_have_a_volatility_but_no_target_until_there_is_history()
    {
        var reading = VolTarget.Read([.. Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? 0.01 : -0.01)], new SizingOptions());

        reading.Status.ShouldBe("insufficient_history");
        reading.Sigma.ShouldNotBeNull();
        reading.Scale.ShouldBeNull();
    }

    [Theory]
    [InlineData(0.0080, 0.0105, 0.7619)]
    [InlineData(0.0080, 0.0200, 0.5)] // 0.4 floored
    [InlineData(0.0080, 0.0060, 1.0)] // 1.3333 capped at 1.0
    public void Scale_by_hand_is_floored_and_capped(double target, double sigma, double expected)
    {
        VolTarget.Scale(target, sigma, 0.5)!.Value.ShouldBe(expected, 1e-4);
    }

    [Fact]
    public void The_floor_at_a_quarter_lets_a_deep_cut_through_and_at_one_there_is_no_cut()
    {
        VolTarget.Scale(0.0080, 0.0800, 0.25).ShouldBe(0.25);
        VolTarget.Scale(0.0080, 0.0200, 0.25)!.Value.ShouldBe(0.4, 1e-12);
        VolTarget.Scale(0.0080, 0.0800, 1.0).ShouldBe(1.0);
        VolTarget.Scale(0.0080, 0.0200, 1.0).ShouldBe(1.0);
    }

    [Fact]
    public void A_floor_outside_the_allowed_range_is_pulled_into_it_never_below_a_quarter_never_above_one()
    {
        VolTarget.Scale(0.001, 0.1, 0.0).ShouldBe(0.25);
        VolTarget.Scale(0.001, 0.1, -3).ShouldBe(0.25);
        VolTarget.Scale(0.1, 0.001, 9).ShouldBe(1.0);
        VolTarget.Scale(0.001, 0.1, double.NaN).ShouldBe(0.25);
    }

    [Fact]
    public void A_sigma_of_zero_or_unusable_numbers_never_give_a_guess()
    {
        VolTarget.Scale(0.008, 0, 0.5).ShouldBe(1.0);
        VolTarget.Scale(0, 0.01, 0.5).ShouldBeNull();
        VolTarget.Scale(double.NaN, 0.01, 0.5).ShouldBeNull();
        VolTarget.Scale(0.008, -1, 0.5).ShouldBeNull();
        VolTarget.Scale(0.008, double.PositiveInfinity, 0.5).ShouldBeNull();
    }

    [Fact]
    public void The_ceiling_is_one_and_a_constant()
    {
        VolTarget.Ceiling.ShouldBe(1.0);
    }

    [Fact]
    public void Property_the_scale_is_always_between_the_floor_and_one()
    {
        var random = new Random(10);
        for (var i = 0; i < 5000; i++)
        {
            var floor = 0.25 + (random.NextDouble() * 0.75);
            var target = random.NextDouble() * 0.1;
            var sigma = random.NextDouble() * 0.2;

            var scale = VolTarget.Scale(target, sigma, floor);

            if (scale is { } s)
            {
                s.ShouldBeGreaterThanOrEqualTo(floor - 1e-12);
                s.ShouldBeLessThanOrEqualTo(1.0);
            }
        }
    }

    [Fact]
    public void Property_higher_volatility_never_gives_a_larger_scale()
    {
        var random = new Random(11);
        for (var i = 0; i < 2000; i++)
        {
            var target = 0.001 + (random.NextDouble() * 0.05);
            var low = random.NextDouble() * 0.1;
            var high = low + (random.NextDouble() * 0.1);

            VolTarget.Scale(target, high, 0.5)!.Value.ShouldBeLessThanOrEqualTo(VolTarget.Scale(target, low, 0.5)!.Value + 1e-12);
        }
    }

    [Fact]
    public void A_calm_history_then_a_volatile_week_gives_a_scale_below_one()
    {
        var returns = new List<double>();
        for (var i = 0; i < 300; i++)
        {
            returns.Add(i % 2 == 0 ? 0.005 : -0.005);
        }

        returns.AddRange([0.06, -0.05, 0.06, -0.05, 0.06]);

        var reading = VolTarget.Read(returns, new SizingOptions());

        reading.Status.ShouldBe(VolReading.Ok);
        reading.Scale!.Value.ShouldBeLessThan(1.0);
        reading.Scale.Value.ShouldBeGreaterThanOrEqualTo(0.5);
        reading.Ratio!.Value.ShouldBeGreaterThan(1.0);
    }

    [Fact]
    public void A_calm_series_is_scale_one_and_non_finite_returns_are_dropped()
    {
        var returns = new List<double> { double.NaN, double.PositiveInfinity };
        returns.AddRange(Enumerable.Range(0, 300).Select(i => i % 2 == 0 ? 0.005 : -0.005));

        var reading = VolTarget.Read(returns, new SizingOptions());

        reading.Returns.ShouldBe(300);
        reading.Scale.ShouldBe(1.0);
    }
}

using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03 part B, criterion 15: the per-variant daily series are aligned on one set of dates, summed per day, rounded to four places and finite.</summary>
public sealed class AlignedSeriesTests
{
    private static readonly DateOnly D1 = new(2026, 1, 5);
    private static readonly DateOnly D2 = new(2026, 1, 6);
    private static readonly DateOnly D3 = new(2026, 1, 7);

    [Fact]
    public void Variants_are_aligned_on_the_union_of_their_dates_with_zero_on_a_day_without_an_exit()
    {
        var aligned = AlignedSeries.Align([[(D3, 1.0), (D1, 2.0)], [(D2, -0.5)]]);

        aligned.Unavailable.ShouldBeNull();
        aligned.Dates.ShouldBe([D1, D2, D3]);
        aligned.Values[0].ShouldBe([2.0, 0.0, 1.0]);
        aligned.Values[1].ShouldBe([0.0, -0.5, 0.0]);
    }

    [Fact]
    public void Trades_that_exit_on_one_day_are_summed()
    {
        var aligned = AlignedSeries.Align([[(D1, 1.0), (D1, -0.25), (D2, 0.5)]]);

        aligned.Values[0].ShouldBe([0.75, 0.5]);
    }

    [Fact]
    public void Values_are_rounded_to_four_places_and_a_negative_zero_is_a_zero()
    {
        var aligned = AlignedSeries.Align([[(D1, 0.123456), (D2, -0.00001), (D3, 0.00004)]]);

        aligned.Values[0][0].ShouldBe(0.1235);
        double.IsNegative(aligned.Values[0][1]).ShouldBeFalse();
        aligned.Values[0][1].ShouldBe(0.0);
        aligned.Values[0][2].ShouldBe(0.0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_finite_makes_the_set_unavailable_with_no_series(double bad)
    {
        var aligned = AlignedSeries.Align([[(D1, 1.0)], [(D1, 2.0), (D2, bad)]]);

        aligned.Unavailable.ShouldBe("unavailable: a daily value is not a finite number");
        aligned.Dates.ShouldBeEmpty();
        aligned.Values.ShouldBeEmpty();
    }

    [Fact]
    public void Two_finite_values_that_add_up_to_infinity_are_unavailable_too()
    {
        var aligned = AlignedSeries.Align([[(D1, double.MaxValue), (D1, double.MaxValue)]]);

        aligned.Unavailable.ShouldNotBeNull();
    }

    [Fact]
    public void No_variant_gives_no_dates_and_no_series_but_no_problem()
    {
        var aligned = AlignedSeries.Align([]);

        aligned.Unavailable.ShouldBeNull();
        aligned.Dates.ShouldBeEmpty();
        aligned.Values.ShouldBeEmpty();
    }
}

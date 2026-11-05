using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-07 part B, criterion 5 (ADR 0209): the rank of the real strategy among the placebo shapes by hand. Rank 1 of 21 is p = 1/21 and passes; a tie goes against the strategy; fewer than 19 comparable placebos is inconclusive.</summary>
public sealed class PlaceboSetTests
{
    private static readonly NullOptions Options = new();

    private static List<PlaceboReading> Readings(Func<int, double?> mean, int placed = 300) =>
        [.. PlaceboShapes.All.Select((shape, i) => new PlaceboReading(shape.Name, mean(i) is null ? 0 : placed, mean(i)))];

    [Fact]
    public void A_real_mean_of_plus_012_above_all_twenty_placebos_ranks_first_with_p_one_over_twenty_one_and_passes()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => -0.05 + (i * 0.005)), Options);

        set.Status.ShouldBe(NullStatus.Pass);
        (set.Shapes, set.Comparable, set.RealRank).ShouldBe((20, 20, 1));
        set.PValue!.Value.ShouldBe(1.0 / 21.0, 1e-12);
        set.PValue!.Value.ShouldBe(0.0476, 1e-4);
        set.Threshold.ShouldBe(0.05);
    }

    [Fact]
    public void A_best_placebo_of_plus_013_puts_the_strategy_second_with_p_two_over_twenty_one_and_fails()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 7 ? 0.13 : 0.0), Options);

        set.Status.ShouldBe(NullStatus.Fail);
        set.RealRank.ShouldBe(2);
        set.PValue!.Value.ShouldBe(2.0 / 21.0, 1e-12);
        set.PValue!.Value.ShouldBe(0.0952, 1e-4);
        set.Rows[7].Rank.ShouldBe(1);
        set.Rows.Where(r => r.Shape != PlaceboShapes.All[7].Name).ShouldAllBe(r => r.Rank > 2);
    }

    [Fact]
    public void A_placebo_that_ties_the_real_mean_goes_ahead_of_it()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 0 ? 0.12 : -0.1), Options);

        set.RealRank.ShouldBe(2);
        set.Status.ShouldBe(NullStatus.Fail);
        set.Rows[0].Rank.ShouldBe(1);
    }

    [Fact]
    public void Only_eighteen_placebos_computable_is_inconclusive_whatever_the_ranks()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i < 2 ? null : -0.5), Options);

        set.Status.ShouldBe(NullStatus.Unavailable);
        set.Comparable.ShouldBe(18);
        (set.RealRank, set.PValue).ShouldBe((null, null));
        set.Reason.ShouldContain("inconclusive");
        set.Rows.Take(2).ShouldAllBe(r => !r.Comparable && r.Rank == null && r.Reason == "no trade could be placed");
    }

    [Fact]
    public void Nineteen_computable_are_enough_and_first_place_is_one_in_twenty_which_is_at_the_threshold()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 3 ? null : -0.5), Options);

        set.Status.ShouldBe(NullStatus.Pass);
        set.PValue!.Value.ShouldBe(0.05);
        set.Comparable.ShouldBe(19);
    }

    [Fact]
    public void Nineteen_comparable_with_rank_two_is_two_in_twenty_and_fails()
    {
        var set = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 3 ? null : i == 5 ? 0.5 : -0.5), Options);

        set.Status.ShouldBe(NullStatus.Fail);
        set.PValue!.Value.ShouldBe(0.1, 1e-12);
    }

    [Fact]
    public void A_shape_that_placed_fewer_trades_than_the_minimum_is_left_out_with_the_reason_and_is_not_ranked()
    {
        var readings = Readings(_ => -0.5);
        readings[4] = new PlaceboReading(readings[4].Name, 12, 9.9);

        var set = PlaceboSet.Evaluate(0.12, 300, 30, readings, Options);

        set.Comparable.ShouldBe(19);
        set.Rows[4].Comparable.ShouldBeFalse();
        set.Rows[4].Reason.ShouldBe("only 12 trades could be placed (at least 30 are needed)");
        set.Rows[4].MeanR.ShouldBe(9.9, "the figure is shown, it is only not ranked");
        set.Status.ShouldBe(NullStatus.Pass, "the thin shape's 9.9 R does not outrank the strategy");
    }

    [Fact]
    public void Too_few_real_trades_or_a_real_mean_that_is_not_finite_is_unavailable()
    {
        PlaceboSet.Evaluate(0.12, 20, 30, Readings(_ => -0.5), Options).Status.ShouldBe(NullStatus.Unavailable);
        PlaceboSet.Evaluate(double.NaN, 300, 30, Readings(_ => -0.5), Options).Status.ShouldBe(NullStatus.Unavailable);
        PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 2 ? double.PositiveInfinity : -0.5), Options).Comparable.ShouldBe(19);
    }

    [Fact]
    public void Asking_for_all_twenty_makes_nineteen_inconclusive()
    {
        var readings = Readings(i => i == 3 ? null : -0.5);

        PlaceboSet.Evaluate(0.12, 300, 30, readings, new NullOptions(MinPlacebos: 20)).Status.ShouldBe(NullStatus.Unavailable);
        PlaceboSet.Evaluate(0.12, 300, 30, readings, new NullOptions(MinPlacebos: 19)).Status.ShouldBe(NullStatus.Pass);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(95)]
    [InlineData(99)]
    public void The_placebo_level_does_not_change_with_the_percentile_of_the_random_entry_null(double percentile)
    {
        var options = new NullOptions(Percentile: percentile);
        var first = PlaceboSet.Evaluate(0.12, 300, 30, Readings(_ => -0.5), options);
        var second = PlaceboSet.Evaluate(0.12, 300, 30, Readings(i => i == 7 ? 0.13 : -0.5), options);

        first.Threshold.ShouldBe(0.05);
        first.Status.ShouldBe(NullStatus.Pass, "rank 1 of 21 is 0.0476");
        second.Threshold.ShouldBe(0.05);
        second.Status.ShouldBe(NullStatus.Fail, "rank 2 of 21 is 0.0952 even at percentile 90");
    }

    [Fact]
    public void A_roll_that_lands_on_its_own_instrument_gives_the_structural_reason_not_the_general_one()
    {
        var readings = Readings(i => i == 17 ? null : -0.5);
        readings[17] = readings[17] with { NotPlacedReason = "the roll lands on the signal's own instrument (only 3 instruments have a trade)" };
        readings[3] = new PlaceboReading(readings[3].Name, 0, null);

        var set = PlaceboSet.Evaluate(0.12, 300, 30, readings, new NullOptions(MinPlacebos: 19));

        set.Rows[17].Reason.ShouldBe("the roll lands on the signal's own instrument (only 3 instruments have a trade)");
        set.Rows[3].Reason.ShouldBe("no trade could be placed");
    }

    [Fact]
    public void Row_ranks_place_the_real_strategy_among_the_placebos_and_a_lower_mean_ranks_lower()
    {
        var set = PlaceboSet.Evaluate(0.0, 300, 30, Readings(i => i - 10.0), Options);

        set.Rows[19].Rank.ShouldBe(1);
        set.Rows[10].Rank.ShouldBe(10, "9 placebos above, the real strategy at 0 ties with shape 10 and goes after it");
        set.Rows[0].Rank.ShouldBe(21);
        set.RealRank.ShouldBe(11);
    }
}

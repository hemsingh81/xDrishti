using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10c-1 (ADR 0216), spec AC5 and D4: the paired day-cluster bootstrap, against hand-worked numbers.</summary>
public sealed class PairedDayBootstrapTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    private static DateOnly Day(int i) => Start.AddDays(i);

    private static PairedBootstrapResult Run(double[] gains, DateOnly[] days, int draws = 20000, string key = "k", int minDays = 2) =>
        PairedDayBootstrap.Assess(gains, days, draws, 0.90, key, minDays).Value;

    [Fact]
    public void Four_days_of_2_1_minus1_3_give_237_of_256_by_full_enumeration_and_the_seeded_run_agrees()
    {
        double[] g = [2, 1, -1, 3];
        var better = 0;
        for (var a = 0; a < 4; a++)
        {
            for (var b = 0; b < 4; b++)
            {
                for (var c = 0; c < 4; c++)
                {
                    for (var d = 0; d < 4; d++)
                    {
                        if (g[a] + g[b] + g[c] + g[d] > 0)
                        {
                            better++;
                        }
                    }
                }
            }
        }

        var exact = better / 256.0;
        var r = Run(g, [Day(0), Day(1), Day(2), Day(3)]);

        better.ShouldBe(237);
        exact.ShouldBe(0.92578125);
        Math.Abs(r.ProbabilityBetter - exact).ShouldBeLessThan(0.01);
        r.Reading(0.95, 0.90).ShouldBe(PairedReading.Promising); // 0.9258: promising, never a pass
        r.MeanGain.ShouldBe(1.25);
        r.GainLow.ShouldBeLessThanOrEqualTo(0); // P(mean <= 0) is 7.4 percent, so the 5th percentile is not above 0
        r.GainHigh.ShouldBeGreaterThan(1.25);
        (r.Rows, r.Days, r.Draws, r.Level).ShouldBe((4, 4, 20000, 0.90));
    }

    [Fact]
    public void Two_days_of_plus1_and_minus1_give_a_quarter_because_a_tied_mean_is_not_better()
    {
        // the four resamples: (+,+) is better; (+,-), (-,+) tie at 0; (-,-) is worse
        var r = Run([1, -1], [Day(0), Day(1)]);

        Math.Abs(r.ProbabilityBetter - 0.25).ShouldBeLessThan(0.01);
    }

    [Fact]
    public void The_mean_is_taken_over_rows_so_a_day_with_more_trades_weighs_more()
    {
        // day A: three rows of +1; day B: one row of -1. Resamples AA +1, AB (3 - 1) / 4 > 0, BA the same, BB -1: three of four are better (a mean of day means would tie on AB)
        var r = Run([1, 1, 1, -1], [Day(0), Day(0), Day(0), Day(1)]);

        Math.Abs(r.ProbabilityBetter - 0.75).ShouldBeLessThan(0.01);
        r.Days.ShouldBe(2);
        r.Rows.ShouldBe(4);
    }

    [Fact]
    public void Gains_that_are_all_the_same_give_zero_variance_an_interval_of_one_point_and_a_probability_of_one_or_zero()
    {
        var days = Enumerable.Range(0, 20).Select(Day).ToArray();

        var positive = Run([.. days.Select(_ => 0.5)], days, 1000);
        var zero = Run([.. days.Select(_ => 0.0)], days, 1000);
        var negative = Run([.. days.Select(_ => -0.5)], days, 1000);

        (positive.ProbabilityBetter, positive.GainLow, positive.GainHigh).ShouldBe((1.0, 0.5, 0.5));
        (zero.ProbabilityBetter, zero.GainLow, zero.GainHigh).ShouldBe((0.0, 0.0, 0.0)); // a gain of exactly zero is a tie, not better
        (negative.ProbabilityBetter, negative.GainHigh).ShouldBe((0.0, -0.5));
    }

    [Fact]
    public void A_sum_that_cancels_only_up_to_rounding_is_a_tie()
    {
        // 0.1 + 0.2 - 0.3 is 5.5e-17 in doubles: a mean that small is zero, not better
        var r = Run([0.1, 0.2, -0.3, 0.1, 0.2, -0.3], [Day(0), Day(0), Day(0), Day(1), Day(1), Day(1)], 1000);

        r.ProbabilityBetter.ShouldBe(0);
    }

    [Fact]
    public void The_same_rows_and_key_repeat_a_different_key_gives_other_draws_and_input_order_does_not_matter()
    {
        var days = Enumerable.Range(0, 40).Select(Day).ToArray();
        var gains = Enumerable.Range(0, 40).Select(i => ((i * 7) % 11) - 4.0).ToArray();

        var a = Run(gains, days, 2000, "model|a");
        var again = Run(gains, days, 2000, "model|a");
        var other = Run(gains, days, 2000, "model|b");
        var shuffled = Run([.. gains.Reverse()], [.. days.Reverse()], 2000, "model|a");

        again.ShouldBe(a);
        shuffled.ShouldBe(a);
        other.ShouldNotBe(a);
    }

    [Fact]
    public void Fewer_days_than_asked_for_is_not_available_never_a_probability_of_one()
    {
        var days = Enumerable.Range(0, 9).Select(Day).ToArray();

        var few = PairedDayBootstrap.Assess([.. days.Select(_ => 1.0)], days, 1000, 0.90, "k", 10);
        var one = PairedDayBootstrap.Assess([1.0, 2.0], [Day(0), Day(0)], 1000, 0.90, "k");

        few.IsFailure.ShouldBeTrue();
        few.Error.Code.ShouldBe(PairedDayBootstrap.TooFewDaysCode);
        one.Error.Code.ShouldBe(PairedDayBootstrap.TooFewDaysCode);
    }

    [Theory]
    [InlineData(0, 3, 3, 1000, 0.9, 2)] // no rows
    [InlineData(1, 3, 3, 1000, 0.9, 2)] // length mismatch
    [InlineData(2, 3, 3, 99, 0.9, 2)] // too few draws
    [InlineData(3, 3, 3, 20001, 0.9, 2)] // too many draws
    [InlineData(4, 3, 3, 1000, 0.5, 2)] // level
    [InlineData(5, 3, 3, 1000, 1.0, 2)] // level
    [InlineData(6, 3, 3, 1000, 0.9, 1)] // minDays
    public void Input_that_cannot_be_read_is_refused_with_a_code(int caseNo, int rows, int dayCount, int draws, double level, int minDays)
    {
        var gains = caseNo == 0 ? [] : Enumerable.Range(0, rows).Select(i => i + 1.0).ToArray();
        var days = caseNo == 0 ? [] : Enumerable.Range(0, caseNo == 1 ? dayCount - 1 : dayCount).Select(Day).ToArray();

        var r = PairedDayBootstrap.Assess(gains, days, draws, level, "k", minDays);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(PairedDayBootstrap.InvalidCode);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_gain_that_is_not_a_number_is_refused(double bad)
    {
        PairedDayBootstrap.Assess([1.0, bad, 2.0], [Day(0), Day(1), Day(2)], 1000, 0.90, "k").Error.Code.ShouldBe(PairedDayBootstrap.InvalidCode);
    }

    [Fact]
    public void The_reading_follows_the_bar_and_the_promising_level()
    {
        PairedBootstrapResult R(double p) => new(10, 10, 1000, 0.9, 0, p, 0, 0);

        R(0.95).Reading(0.95, 0.90).ShouldBe(PairedReading.Pass);
        R(0.9499).Reading(0.95, 0.90).ShouldBe(PairedReading.Promising);
        R(0.90).Reading(0.95, 0.90).ShouldBe(PairedReading.Promising);
        R(0.8999).Reading(0.95, 0.90).ShouldBe(PairedReading.Below);
        R(0.9258).ToText(PairedReading.Promising).ShouldBe("P(better) 0.9258 (promising), mean gain 0.0000 per trade, 90% interval [0.0000, 0.0000], 10 days, 1000 draws");
    }

    [Fact]
    public void The_row_log_loss_is_the_one_the_metrics_average()
    {
        double[] scores = [0.2, 0.7, 0.5, 0.0, 1.0];
        int[] labels = [0, 1, 1, 1, 0];

        MetricsCalculator.RowLogLoss(0.7, 1).ShouldBe(-Math.Log(0.7));
        scores.Select((s, i) => MetricsCalculator.RowLogLoss(s, labels[i])).Average().ShouldBe(MetricsCalculator.Compute(scores, labels).LogLoss, 1e-12);
        double.IsFinite(MetricsCalculator.RowLogLoss(0.0, 1)).ShouldBeTrue(); // clamped away from 0 and 1
    }
}

using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class StitchingCheckTests
{
    private static readonly OptionQualityThresholds T = new();

    private static readonly DateTimeOffset Open = new(2021, 12, 6, 9, 15, 0, TimeSpan.FromMinutes(330));

    private const int Step = 5_000;

    private const int Base = 1_750_000;

    private static List<RollingPoint> Bars(int count, int interval = 5, int spot = Base, int? close = null) =>
        [.. Enumerable.Range(0, count).Select(i => new RollingPoint(Open.AddMinutes(i * interval), Base, spot, close ?? spot))];

    /// <summary>Every bar from <paramref name="from"/> on holds a strike <paramref name="steps"/> steps higher: one move between two bars.</summary>
    private static List<RollingPoint> Moved(List<RollingPoint> bars, int from, int steps) => [.. bars.Select((b, i) => i >= from ? b with { StrikePx = b.StrikePx + (steps * Step) } : b)];

    [Fact]
    public void A_steady_series_with_matching_spot_passes()
    {
        var check = StitchingCheck.Evaluate(Bars(75), 75, Step, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.Reason.ShouldBeNull();
        check.Detail.ShouldNotBeNull().ShouldContain("\"spot_mismatches\":0");
    }

    [Fact]
    public void A_move_of_two_steps_is_allowed()
    {
        StitchingCheck.Evaluate(Moved(Bars(375, 1), 10, 2), 375, Step, T).Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void A_move_of_three_steps_is_a_strike_jump_that_warns_when_it_is_up_to_one_percent_of_the_bars()
    {
        var check = StitchingCheck.Evaluate(Moved(Bars(375, 1), 10, 3), 375, Step, T);

        check.Status.ShouldBe(QualityStatus.Warn); // 1 of 375 bars
        check.Detail.ShouldNotBeNull().ShouldContain("\"strike_jumps\":1");
    }

    [Fact]
    public void The_same_jump_in_a_five_minute_session_of_75_bars_is_above_one_percent_and_fails()
    {
        StitchingCheck.Evaluate(Moved(Bars(75), 10, 3), 75, Step, T).Status.ShouldBe(QualityStatus.Fail); // 1 of 75 = 1.33 %
    }

    [Fact]
    public void Missing_bars_are_counted_from_the_expected_number()
    {
        var check = StitchingCheck.Evaluate(Bars(70), 75, Step, T);

        check.Detail.ShouldNotBeNull().ShouldContain("\"missing_bars\":5");
        check.Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void A_spot_more_than_half_a_percent_from_the_underlying_close_is_a_mismatch_and_over_one_percent_of_bars_warns()
    {
        var bars = Bars(75, close: Base + (Base / 100)); // 1 % off on every bar

        var check = StitchingCheck.Evaluate(bars, 75, Step, T);

        check.Status.ShouldBe(QualityStatus.Warn);
        check.Detail.ShouldNotBeNull().ShouldContain("\"spot_mismatches\":75");
    }

    [Fact]
    public void A_spot_inside_half_a_percent_is_no_mismatch()
    {
        StitchingCheck.Evaluate(Bars(75, close: Base + (Base / 400)), 75, Step, T).Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void No_one_minute_bar_skips_the_comparison_and_says_so_without_failing_the_check()
    {
        var bars = Bars(75).Select(b => b with { UnderlyingClosePx = null }).ToList();

        var check = StitchingCheck.Evaluate(bars, 75, Step, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.Reason.ShouldBe("no_underlying_bar");
        check.Detail.ShouldNotBeNull().ShouldContain("\"spot_compared\":0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void An_unknown_strike_step_is_unavailable(int? step)
    {
        var check = StitchingCheck.Evaluate(Bars(75), 75, step, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("no_strike_step");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_bars_is_unavailable(int count)
    {
        var check = StitchingCheck.Evaluate(Bars(count), 75, Step, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("too_few_points");
    }
}

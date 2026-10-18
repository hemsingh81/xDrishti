using XDrishti.Domain.Market;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The functions every compiled condition is made of, walked through exhaustively: Kleene's tables written out by hand, every comparison over a set of numbers that includes NaN, both infinities and both zeros, and
/// the lagged builtins over every combination of a few values with a NaN in every position.
/// </summary>
public sealed class KernelTests
{
    private const char T = 'T';

    private const char F = 'F';

    private const char U = 'U';

    private static readonly int[] Order = [Kernel.False, Kernel.Unknown, Kernel.True];

    private static readonly double[] Numbers = [double.NaN, double.NegativeInfinity, -1, -0.0, 0, 1, double.PositiveInfinity];

    private static char Letter(int truth) => truth switch { Kernel.True => T, Kernel.False => F, Kernel.Unknown => U, _ => throw new InvalidOperationException("Not a truth value: " + truth) };

    // ---- the numbers ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_truth_numbers_are_the_ones_of_the_public_enum_and_the_enum_is_a_signed_byte()
    {
        Kernel.False.ShouldBe((int)Truth.False);
        Kernel.True.ShouldBe((int)Truth.True);
        Kernel.Unknown.ShouldBe((int)Truth.Unknown);
        ((int)Truth.False, (int)Truth.True, (int)Truth.Unknown).ShouldBe((0, 1, -1));
        Enum.GetUnderlyingType(typeof(Truth)).ShouldBe(typeof(sbyte));
        Enum.GetValues<Truth>().Length.ShouldBe(3);
    }

    [Fact]
    public void The_offset_to_india_time_is_the_one_of_the_domain()
    {
        var offset = (long)IndiaTime.Offset.TotalSeconds;
        offset.ShouldBe(19_800);

        // the minute at which the bar that starts at epoch t closes, worked out here from the domain's offset: (t + 60 s + offset) as minutes of the day
        foreach (var start in new long[] { 0, 60, 1_000_000_020, 1_700_000_040, -60, -19_800, -86_460 })
        {
            var expected = (int)((((start + 60 + offset) % 86_400) + 86_400) % 86_400 / 60);

            CloseMinuteAfter(start).ShouldBe(expected, "a bar that starts at epoch second " + start);
        }
    }

    [Fact]
    public void Every_function_the_expression_trees_call_exists()
    {
        KernelMethods.Not.Name.ShouldBe(nameof(Kernel.Not));
        KernelMethods.CloseMinute.DeclaringType.ShouldBe(typeof(Kernel));
        KernelMethods.Abs.DeclaringType.ShouldBe(typeof(Math));
        new[] { KernelMethods.Min, KernelMethods.Max }.ShouldAllBe(m => m.GetParameters().Length == 2 && m.ReturnType == typeof(double));
    }

    // ---- Kleene's tables, by hand ----------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Rows are the left operand in the order false, unknown, true; columns the right operand in the same order.
    private static readonly string[] AndTable = ["FFF", "FUU", "FUT"];

    private static readonly string[] OrTable = ["FUT", "UUT", "TTT"];

    private static readonly string[] EqualTable = ["TUF", "UUU", "FUT"];

    private static readonly string[] NotEqualTable = ["FUT", "UUU", "TUF"];

    [Fact]
    public void And_follows_kleenes_table_in_all_nine_cases()
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Letter(Kernel.And(Order[row], Order[column])).ShouldBe(AndTable[row][column], $"{Letter(Order[row])} and {Letter(Order[column])}");
            }
        }
    }

    [Fact]
    public void Or_follows_kleenes_table_in_all_nine_cases()
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Letter(Kernel.Or(Order[row], Order[column])).ShouldBe(OrTable[row][column], $"{Letter(Order[row])} or {Letter(Order[column])}");
            }
        }
    }

    [Fact]
    public void Not_swaps_true_and_false_and_leaves_unknown()
    {
        Kernel.Not(Kernel.True).ShouldBe(Kernel.False);
        Kernel.Not(Kernel.False).ShouldBe(Kernel.True);
        Kernel.Not(Kernel.Unknown).ShouldBe(Kernel.Unknown);
    }

    [Fact]
    public void Equality_of_two_truth_values_is_unknown_when_either_is_unknown()
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Letter(Kernel.SameTruth(Order[row], Order[column])).ShouldBe(EqualTable[row][column], $"{Letter(Order[row])} == {Letter(Order[column])}");
                Letter(Kernel.DifferentTruth(Order[row], Order[column])).ShouldBe(NotEqualTable[row][column], $"{Letter(Order[row])} != {Letter(Order[column])}");
            }
        }
    }

    [Fact]
    public void The_five_sentences_of_the_spec_hold()
    {
        // "false and unknown = false, true and unknown = unknown, true or unknown = true, false or unknown = unknown, not unknown = unknown"
        Kernel.And(Kernel.False, Kernel.Unknown).ShouldBe(Kernel.False);
        Kernel.And(Kernel.Unknown, Kernel.False).ShouldBe(Kernel.False);
        Kernel.And(Kernel.True, Kernel.Unknown).ShouldBe(Kernel.Unknown);
        Kernel.Or(Kernel.True, Kernel.Unknown).ShouldBe(Kernel.True);
        Kernel.Or(Kernel.Unknown, Kernel.True).ShouldBe(Kernel.True);
        Kernel.Or(Kernel.False, Kernel.Unknown).ShouldBe(Kernel.Unknown);
        Kernel.Not(Kernel.Unknown).ShouldBe(Kernel.Unknown);
    }

    [Fact]
    public void De_morgans_laws_hold_in_all_nine_cases_and_not_is_its_own_inverse()
    {
        foreach (var a in Order)
        {
            Kernel.Not(Kernel.Not(a)).ShouldBe(a);
            foreach (var b in Order)
            {
                Kernel.Not(Kernel.And(a, b)).ShouldBe(Kernel.Or(Kernel.Not(a), Kernel.Not(b)));
                Kernel.Not(Kernel.Or(a, b)).ShouldBe(Kernel.And(Kernel.Not(a), Kernel.Not(b)));
                Kernel.And(a, b).ShouldBe(Kernel.And(b, a));
                Kernel.Or(a, b).ShouldBe(Kernel.Or(b, a));
            }
        }
    }

    // ---- comparisons -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<int, int> NumberPairs { get; } = [.. Enumerable.Range(0, 7).SelectMany(i => Enumerable.Range(0, 7).Select(j => (i, j)))];

    [Theory]
    [MemberData(nameof(NumberPairs))]
    public void The_six_comparisons_answer_unknown_for_a_nan_and_the_ordinary_answer_for_two_numbers(int i, int j)
    {
        double a = Numbers[i], b = Numbers[j];
        var anyNaN = double.IsNaN(a) || double.IsNaN(b);
        char Expect(bool holds) => anyNaN ? U : holds ? T : F;

        Letter(Kernel.Less(a, b)).ShouldBe(Expect(a < b), $"{a} < {b}");
        Letter(Kernel.LessOrEqual(a, b)).ShouldBe(Expect(a < b || a == b), $"{a} <= {b}");
        Letter(Kernel.Greater(a, b)).ShouldBe(Expect(a > b), $"{a} > {b}");
        Letter(Kernel.GreaterOrEqual(a, b)).ShouldBe(Expect(a > b || a == b), $"{a} >= {b}");
        Letter(Kernel.Equal(a, b)).ShouldBe(Expect(!(a < b) && !(a > b)), $"{a} == {b}");
        Letter(Kernel.NotEqual(a, b)).ShouldBe(Expect(a < b || a > b), $"{a} != {b}");
    }

    [Fact]
    public void Both_zeros_are_equal_and_infinity_equals_itself()
    {
        Kernel.Equal(0.0, -0.0).ShouldBe(Kernel.True);
        Kernel.Less(-0.0, 0.0).ShouldBe(Kernel.False);
        Kernel.Equal(double.PositiveInfinity, double.PositiveInfinity).ShouldBe(Kernel.True);
        Kernel.Greater(double.PositiveInfinity, double.MaxValue).ShouldBe(Kernel.True);
        Kernel.Equal(double.NaN, double.NaN).ShouldBe(Kernel.Unknown, "a number that is not ready is not equal to itself, it is unknown");
    }

    // ---- crossings -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly double[] Three = [1, 2, 3];

    [Fact]
    public void Crossings_over_every_combination_of_four_values_follow_their_definitions()
    {
        foreach (var a0 in Three)
        {
            foreach (var b0 in Three)
            {
                foreach (var a1 in Three)
                {
                    foreach (var b1 in Three)
                    {
                        // above: the line is above now and was not above (below or equal) one bar ago; below: the mirror
                        var above = a0 > b0 && a1 <= b1;
                        var below = a0 < b0 && a1 >= b1;
                        Letter(Kernel.CrossesAbove(a0, b0, a1, b1)).ShouldBe(above ? T : F, $"above {a0} {b0} {a1} {b1}");
                        Letter(Kernel.CrossesBelow(a0, b0, a1, b1)).ShouldBe(below ? T : F, $"below {a0} {b0} {a1} {b1}");
                    }
                }
            }
        }
    }

    [Fact]
    public void A_crossing_is_unknown_when_any_of_its_four_values_is_nan_whatever_the_other_three_say()
    {
        foreach (var position in Enumerable.Range(0, 4))
        {
            foreach (var other in Three)
            {
                var values = new[] { other, other, other, other };
                values[position] = double.NaN;

                Kernel.CrossesAbove(values[0], values[1], values[2], values[3]).ShouldBe(Kernel.Unknown, "above, nan at " + position);
                Kernel.CrossesBelow(values[0], values[1], values[2], values[3]).ShouldBe(Kernel.Unknown, "below, nan at " + position);
            }
        }

        // the three that are there would already say false (a0 below b0): still unknown
        Kernel.CrossesAbove(1, 2, double.NaN, 1).ShouldBe(Kernel.Unknown);
    }

    // ---- between ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Between_over_every_combination_of_three_values_includes_both_ends()
    {
        foreach (var x in Three)
        {
            foreach (var low in Three)
            {
                foreach (var high in Three)
                {
                    Letter(Kernel.Between(x, low, high)).ShouldBe(low <= x && x <= high ? T : F, $"{x} in [{low}, {high}]");
                }
            }
        }
    }

    [Fact]
    public void Between_is_unknown_when_any_of_its_three_values_is_nan()
    {
        Kernel.Between(double.NaN, 1, 3).ShouldBe(Kernel.Unknown);
        Kernel.Between(2, double.NaN, 3).ShouldBe(Kernel.Unknown);
        Kernel.Between(2, 1, double.NaN).ShouldBe(Kernel.Unknown);
        Kernel.Between(5, double.NaN, 3).ShouldBe(Kernel.Unknown, "outside whatever the missing end was: still unknown");
    }

    // ---- runs ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly double[] RunValues = [double.NaN, 1, 2];

    /// <summary>Every sequence of four values over {NaN, 1, 2}, newest first (the order of x[0], x[1], x[2], x[3]).</summary>
    public static TheoryData<int> Sequences { get; } = [.. Enumerable.Range(0, 81)];

    private static double[] Sequence(int code) => [RunValues[code % 3], RunValues[code / 3 % 3], RunValues[code / 9 % 3], RunValues[code / 27 % 3]];

    private static int Run(double[] newestFirst, bool rising)
    {
        var state = Kernel.RunStart(newestFirst[0]);
        for (var lag = 1; lag < newestFirst.Length; lag++)
        {
            state |= rising ? Kernel.StepRising(newestFirst[lag - 1], newestFirst[lag]) : Kernel.StepFalling(newestFirst[lag - 1], newestFirst[lag]);
        }

        return Kernel.RunResult(state);
    }

    [Theory]
    [MemberData(nameof(Sequences))]
    public void A_run_is_unknown_if_any_value_is_nan_else_true_exactly_when_every_step_is_strict(int code)
    {
        var x = Sequence(code);
        var anyNaN = x.Any(double.IsNaN);
        // rising: x[0] > x[1] > x[2] > x[3]; falling: x[0] < x[1] < x[2] < x[3]
        var rising = x[0] > x[1] && x[1] > x[2] && x[2] > x[3];
        var falling = x[0] < x[1] && x[1] < x[2] && x[2] < x[3];

        Letter(Run(x, rising: true)).ShouldBe(anyNaN ? U : rising ? T : F, "rising " + string.Join(" ", x));
        Letter(Run(x, rising: false)).ShouldBe(anyNaN ? U : falling ? T : F, "falling " + string.Join(" ", x));
    }

    [Fact]
    public void A_run_of_one_step_has_two_values_and_equal_values_are_not_a_step()
    {
        Letter(Kernel.RunResult(Kernel.RunStart(2) | Kernel.StepRising(2, 1))).ShouldBe(T);
        Letter(Kernel.RunResult(Kernel.RunStart(2) | Kernel.StepRising(2, 2))).ShouldBe(F);
        Letter(Kernel.RunResult(Kernel.RunStart(2) | Kernel.StepFalling(1, 2))).ShouldBe(T);
        Letter(Kernel.RunResult(Kernel.RunStart(1) | Kernel.StepFalling(1, 1))).ShouldBe(F);
    }

    // ---- arithmetic ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void Division_by_zero_is_nan_never_an_infinity_or_an_exception(double a)
    {
        double.IsNaN(Kernel.Divide(a, 0.0)).ShouldBeTrue();
        double.IsNaN(Kernel.Divide(a, -0.0)).ShouldBeTrue("negative zero is zero too");
    }

    [Theory]
    [InlineData(6.0, 3.0, 2.0)]
    [InlineData(-6.0, 3.0, -2.0)]
    [InlineData(1.0, double.PositiveInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, 2.0, double.PositiveInfinity)]
    public void Any_other_division_is_the_ordinary_one(double a, double b, double expected)
    {
        Kernel.Divide(a, b).ShouldBe(expected);
    }

    [Fact]
    public void A_nan_divided_by_a_number_stays_nan()
    {
        double.IsNaN(Kernel.Divide(double.NaN, 2)).ShouldBeTrue();
        double.IsNaN(Kernel.Divide(2, double.NaN)).ShouldBeTrue();
    }

    // ---- values ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_value_is_the_one_after_the_last_closed_bar_and_the_value_at_a_lag_is_the_one_after_the_bar_that_many_bars_back()
    {
        var spec = Specs.Close(XDrishti.Domain.Market.Bars.Timeframe.M1);
        var frame = Frames.Of(new XDrishti.Indicators.FrameOptions { HistoryDepth = 4 }, spec);
        var handle = frame.HandleOf(spec);
        foreach (var close in new double[] { 10, 20, 30 })
        {
            frame.Step(Clock.Close(Clock.Mon, Clock.Open + (int)close, close));
        }

        Kernel.Value(frame, handle).ShouldBe(30);
        Kernel.ValueAt(frame, handle, 0).ShouldBe(30, "a lag of 0 is the value itself");
        Kernel.ValueAt(frame, handle, 1).ShouldBe(20);
        Kernel.ValueAt(frame, handle, 2).ShouldBe(10);
        double.IsNaN(Kernel.ValueAt(frame, handle, 3)).ShouldBeTrue("only three bars have closed");
    }

    // ---- time ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static double CloseMinuteAfter(params long[] epochs)
    {
        var frame = Frames.OfClose();
        foreach (var epoch in epochs)
        {
            frame.Step(new XDrishti.Domain.Market.Bars.Bar(epoch, 1, 1, 1, 1, 1));
        }

        return Kernel.CloseMinute(frame);
    }

    [Fact]
    public void Close_minute_is_nan_before_the_first_bar_and_the_minute_after_the_start_of_the_last_bar_afterwards()
    {
        double.IsNaN(Kernel.CloseMinute(Frames.OfClose())).ShouldBeTrue();

        // the bar that starts at 09:44 India time closes at 09:45 = 585 minutes; 15:29 closes at 15:30 = 930
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.At(9, 44))).ShouldBe(585);
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.At(15, 29))).ShouldBe(930);
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.Open)).ShouldBe(556);
    }

    [Fact]
    public void Close_minute_wraps_at_midnight_and_does_not_depend_on_the_date()
    {
        // 23:59 closes at midnight of the next day: minute 0
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.At(23, 59))).ShouldBe(0);
        CloseMinuteAfter(Clock.Epoch(Clock.Fri, Clock.At(10, 0))).ShouldBe(601);
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.At(10, 0)), Clock.Epoch(Clock.Mon, Clock.At(10, 1))).ShouldBe(602, "the last accepted bar counts");
    }

    [Fact]
    public void Close_minute_is_right_for_epochs_before_1970()
    {
        // 1969-12-31 India time: the epoch -19,800 - 120 s is 23:58 India time on 1969-12-31; it closes at 23:59 = 1439
        CloseMinuteAfter(-19_800 - 120).ShouldBe(1_439);
        CloseMinuteAfter(-19_800 - 60).ShouldBe(0, "the bar that starts at 23:59 closes at midnight");
    }

    [Fact]
    public void Close_minute_ignores_a_bar_the_frame_skipped()
    {
        // an older bar after a newer one is skipped by the ordering guard and does not change the clock
        CloseMinuteAfter(Clock.Epoch(Clock.Mon, Clock.At(10, 0)), Clock.Epoch(Clock.Mon, Clock.At(9, 0))).ShouldBe(601);
    }
}

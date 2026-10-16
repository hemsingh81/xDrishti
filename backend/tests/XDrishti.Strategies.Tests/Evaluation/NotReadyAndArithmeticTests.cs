using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 8, last sentence: <c>x / 0</c> and a NaN operand never throw and are never true by accident. Arithmetic with a value that is not ready is not ready; a division by zero is not ready (NaN, not an infinity);
/// the functions <c>abs</c>, <c>min</c> and <c>max</c> pass a NaN on. Every case is evaluated both before the average is ready (two bars of <c>sma(3)</c>) and after.
/// </summary>
public sealed class NotReadyAndArithmeticTests
{
    private static readonly DateOnly Mon = Clock.Mon;

    /// <summary>Evaluates the condition after bars with the closes 10, 10 (an average of three is not ready) and after 10, 10, 10, 10 (it is 10).</summary>
    private static (Truth Early, Truth Late) Evaluate(string source)
    {
        var early = Rig.Of(Timeframe.M1, source);
        var late = Rig.Of(Timeframe.M1, source);
        var earlyTruth = Truth.False;
        var lateTruth = Truth.False;
        foreach (var bar in Clock.Closes(Mon, 10, 10))
        {
            earlyTruth = early.Step(bar)[0];
        }

        foreach (var bar in Clock.Closes(Mon, 10, 10, 10, 10))
        {
            lateTruth = late.Step(bar)[0];
        }

        return (earlyTruth, lateTruth);
    }

    [Theory]
    [InlineData("close / 0 > 1", Truth.Unknown)]
    [InlineData("close / 0 < 1", Truth.Unknown)]
    [InlineData("close / 0 == close / 0", Truth.Unknown)]
    [InlineData("close / 0 != 5", Truth.Unknown)]
    [InlineData("not (close / 0 > 1)", Truth.Unknown)]
    [InlineData("close / (close - close) > 0", Truth.Unknown)]
    [InlineData("0 / 0 > 0", Truth.Unknown)]
    [InlineData("-close / 0 < 0", Truth.Unknown)]
    [InlineData("1 / 0 > 1", Truth.Unknown)]
    [InlineData("close / (3 - 3) > 0", Truth.Unknown)]
    [InlineData("1 / (0 - 0) < 5", Truth.Unknown)]
    [InlineData("between(close / 0, 0, 100)", Truth.Unknown)]
    [InlineData("rising(close / 0, 2)", Truth.Unknown)]
    [InlineData("crosses_above(close / 0, 5)", Truth.Unknown)]
    [InlineData("close / 0 > 1 or close > 5", Truth.True)]
    [InlineData("close / 0 > 1 and close < 5", Truth.False)]
    [InlineData("abs(close / 0) >= 0", Truth.Unknown)]
    [InlineData("min(close / 0, 5) < 10", Truth.Unknown)]
    [InlineData("max(close, close / 0) > 0", Truth.Unknown)]
    public void A_division_by_zero_is_not_ready_and_never_throws(string source, Truth expected)
    {
        var (early, late) = Evaluate(source);

        early.ShouldBe(expected);
        late.ShouldBe(expected, "the same with a ready frame: zero is zero whatever the data");
    }

    [Theory]
    [InlineData("sma(3) + 1 > 0")]
    [InlineData("1 + sma(3) > 0")]
    [InlineData("sma(3) - sma(3) == 0")]
    [InlineData("sma(3) * 0 == 0")]
    [InlineData("0 * sma(3) == 0")]
    [InlineData("sma(3) / 2 > 0")]
    [InlineData("2 / sma(3) > 0")]
    [InlineData("-sma(3) < 0")]
    [InlineData("abs(sma(3)) >= 0")]
    [InlineData("min(sma(3), 5) < 10")]
    [InlineData("min(5, sma(3)) < 10")]
    [InlineData("max(sma(3), 5) > 0")]
    [InlineData("max(5, sma(3)) > 0")]
    [InlineData("(sma(3) + 1) * 2 - 1 > 0")]
    [InlineData("sma(3) == sma(3)")]
    [InlineData("sma(3) != 5")]
    [InlineData("between(sma(3), 0, 100)")]
    [InlineData("between(5, sma(3), 100)")]
    [InlineData("between(5, 0, sma(3))")]
    [InlineData("rising(sma(3) + 1, 1)")]
    [InlineData("falling(close - sma(3), 1)")]
    [InlineData("crosses_above(close, sma(3) + 1)")]
    [InlineData("crosses_below(sma(3) * 2, close)")]
    [InlineData("close[1] + sma(3) > 0")]
    [InlineData("(sma(3) > 0) == (close > 0)")]
    [InlineData("(sma(3) > 0) != (close > 0)")]
    [InlineData("not (not (sma(3) > 0))")]
    public void An_operand_that_is_not_ready_makes_the_condition_unknown_not_false_and_not_true(string source)
    {
        var (early, _) = Evaluate(source);

        early.ShouldBe(Truth.Unknown);
    }

    [Theory]
    [InlineData("sma(3) + 1 > 10", Truth.True)]
    [InlineData("sma(3) - sma(3) == 0", Truth.True)]
    [InlineData("sma(3) * 0 == 0", Truth.True)]
    [InlineData("2 / sma(3) < 1", Truth.True)]
    [InlineData("-sma(3) < 0", Truth.True)]
    [InlineData("abs(-sma(3)) == 10", Truth.True)]
    [InlineData("min(sma(3), 5) == 5", Truth.True)]
    [InlineData("max(sma(3), 5) == 10", Truth.True)]
    [InlineData("between(sma(3), 10, 10)", Truth.True)]
    [InlineData("(sma(3) > 0) == (close > 0)", Truth.True)]
    [InlineData("(sma(3) > 0) != (close > 0)", Truth.False)]
    [InlineData("not (not (sma(3) > 0))", Truth.True)]
    public void The_same_conditions_have_their_ordinary_value_once_the_operand_is_ready(string source, Truth expected)
    {
        var (_, late) = Evaluate(source);

        late.ShouldBe(expected);
    }

    [Fact]
    public void A_number_too_big_for_a_double_becomes_an_infinity_which_is_a_number_and_a_difference_of_two_infinities_is_not_ready()
    {
        // 1e300 * 1e300 overflows to +infinity: it compares as a number. +infinity - +infinity is NaN: not ready.
        var rig = Rig.Of(Timeframe.M1, ["close * close > 0", "close * close == close * close", "close * close - close * close == 0", "close * close / (close * close) > 0", "-close * close < 0"]);

        var truths = rig.Step(Clock.Close(Mon, Clock.Open, 1e300));

        truths.ShouldBe([Truth.True, Truth.True, Truth.Unknown, Truth.Unknown, Truth.True]);
    }

    [Fact]
    public void A_very_small_divisor_that_is_not_zero_overflows_to_an_infinity_and_is_still_a_number()
    {
        var rig = Rig.Of(Timeframe.M1, ["close / 1e-320 > 1", "close / -1e-320 < -1"]);

        rig.Step(Clock.Close(Mon, Clock.Open, 10)).ShouldBe([Truth.True, Truth.True]);
    }

    [Fact]
    public void A_zero_volume_bar_is_a_value_not_a_missing_one()
    {
        var rig = Rig.Of(Timeframe.M1, ["volume > 0", "volume == 0", "not (volume > 0)", "close / volume > 0"]);

        rig.Step(Clock.Close(Mon, Clock.Open, 10, volume: 0)).ShouldBe([Truth.False, Truth.True, Truth.True, Truth.Unknown]);
        rig.Step(Clock.Close(Mon, Clock.Open + 1, 10, volume: 5)).ShouldBe([Truth.True, Truth.False, Truth.False, Truth.True]);
    }

    [Fact]
    public void Evaluate_is_false_for_every_unknown_and_never_throws_on_a_frame_that_has_seen_no_bar()
    {
        var sources = new[] { "close > 0", "sma(3) > 0", "not (close > 0)", "crosses_above(close, open)", "rising(close, 5)", "between(close, 0, 10)", "close / 0 > 1", "time >= \"09:45\"", "close[7] > 0", "(close > 0) == (open > 0)" };
        var rig = Rig.Of(Timeframe.M1, sources);

        rig.Read().ShouldAllBe(t => t == Truth.Unknown);
        rig.Bound.ShouldAllBe(b => !b.Evaluate(rig.Frame));
    }
}

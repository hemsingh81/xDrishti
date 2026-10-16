using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The six comparison operators and the arithmetic of the compiled conditions against values worked out by hand, conditions that read no data, and the deepest nesting the parser lets through (a delegate is built from a tree
/// of 60 levels without a stack overflow).
/// </summary>
public sealed class OperatorsAndConstantsTests
{
    private static readonly DateOnly Mon = Clock.Mon;

    [Theory]
    [InlineData("<", "TFF")]
    [InlineData("<=", "TTF")]
    [InlineData(">", "FFT")]
    [InlineData(">=", "FTT")]
    [InlineData("==", "FTF")]
    [InlineData("!=", "TFT")]
    public void Each_comparison_operator_is_the_operator_it_is_written_as(string op, string expected)
    {
        // closes 9, 10, 11 against 10
        Rig.Of(Timeframe.M1, $"close {op} 10").Run(Clock.Closes(Mon, 9, 10, 11)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("<", "FFT")]
    [InlineData("<=", "FTT")]
    [InlineData(">", "TFF")]
    [InlineData(">=", "TTF")]
    [InlineData("==", "FTF")]
    [InlineData("!=", "TFT")]
    public void With_the_number_on_the_left_the_operator_reads_the_other_way(string op, string expected)
    {
        Rig.Of(Timeframe.M1, $"10 {op} close").Run(Clock.Closes(Mon, 9, 10, 11)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2 + close * 3 == 29")]
    [InlineData("close - 1 - 1 == 7")]
    [InlineData("close - (2 - 1) == 8")]
    [InlineData("close / 2 / 3 == 1.5")]
    [InlineData("close / (2 / 3) == 13.5")]
    [InlineData("-close + 20 == 11")]
    [InlineData("--close == 9")]
    [InlineData("abs(close - 20) == 11")]
    [InlineData("abs(-close) == 9")]
    [InlineData("min(close, 5) == 5")]
    [InlineData("max(close, 50) == 50")]
    [InlineData("max(min(close, 12), 3) == 9")]
    [InlineData("(close + 1) * 2 == 20")]
    [InlineData("close * close - 81 == 0")]
    public void Arithmetic_gives_the_value_worked_out_by_hand_for_a_close_of_9(string source)
    {
        Rig.Of(Timeframe.M1, source).Run(Clock.Closes(Mon, 9)).ShouldBe("T", source);
    }

    [Theory]
    [InlineData("rising(1, 200)", "F")]
    [InlineData("falling(1, 3)", "F")]
    [InlineData("crosses_above(1, 2)", "F")]
    [InlineData("crosses_above(2, 1)", "F")]
    [InlineData("crosses_below(1, 2)", "F")]
    [InlineData("between(5, 1, 10)", "T")]
    [InlineData("between(11, 1, 10)", "F")]
    [InlineData("1 < 2", "T")]
    [InlineData("2 < 1", "F")]
    [InlineData("1 + 1 == 2", "T")]
    [InlineData("true", "T")]
    [InlineData("false", "F")]
    [InlineData("not true", "F")]
    [InlineData("true and false", "F")]
    [InlineData("true or false", "T")]
    [InlineData("true == true", "T")]
    [InlineData("true != true", "F")]
    [InlineData("(1 < 2) == (3 < 4)", "T")]
    [InlineData("1 / 0 > 1", "U")]
    [InlineData("abs(-3) == min(3, 4)", "T")]
    public void A_condition_that_reads_no_data_has_the_same_value_at_every_bar(string source, string expected)
    {
        var rig = Rig.Of(Timeframe.M5, source);

        rig.Run(Series.Sessions(1, 1).Take(30)).ShouldBe(string.Concat(Enumerable.Repeat(expected, 30)));
    }

    [Fact]
    public void A_parameter_is_a_constant_of_the_condition_and_two_maps_give_two_independent_conditions()
    {
        var sixty = Compiling.Condition("close > level", Timeframe.M1, Compiling.Params(("level", 60)));
        var forty = Compiling.Condition("close > level", Timeframe.M1, Compiling.Params(("level", 40)));
        var frame60 = Frames.OfClose();
        var frame40 = Frames.OfClose();
        var bound60 = sixty.Bind(frame60);
        var bound40 = forty.Bind(frame40);
        var word60 = string.Empty;
        var word40 = string.Empty;

        foreach (var bar in Clock.Closes(Mon, 30, 50, 70))
        {
            frame60.Step(bar);
            frame40.Step(bar);
            word60 += Rig.Word([bound60.EvaluateTruth(frame60)]);
            word40 += Rig.Word([bound40.EvaluateTruth(frame40)]);
        }

        word60.ShouldBe("FFT");
        word40.ShouldBe("FTT");
    }

    [Fact]
    public void A_condition_nested_as_deep_as_the_parser_allows_compiles_and_evaluates()
    {
        // 62 brackets around a number, 63 conditions joined by "and" (a tree 63 levels deep), 30 and 31 nested "not (…)" and a sum of 60 ones
        var parentheses = "close > " + new string('(', 62) + "5" + new string(')', 62);
        var chain = string.Join(" and ", Enumerable.Range(0, 63).Select(i => $"close > {i % 5}"));
        var evenNots = string.Concat(Enumerable.Repeat("not (", 30)) + "close > 5" + new string(')', 30);
        var oddNots = string.Concat(Enumerable.Repeat("not (", 31)) + "close > 5" + new string(')', 31);
        var sums = "close > " + string.Join(" + ", Enumerable.Repeat("1", 60));

        var rig = Rig.Of(Timeframe.M1, [parentheses, chain, evenNots, oddNots, sums]);

        // 100 is above 5, above 0 to 4 and above 60; 4 is above none of them but 0 to 3 (the chain needs all, and one of its terms is "close > 4")
        rig.Step(Clock.Close(Mon, Clock.Open, 100)).ShouldBe([Truth.True, Truth.True, Truth.True, Truth.False, Truth.True]);
        rig.Step(Clock.Close(Mon, Clock.Open + 1, 4)).ShouldBe([Truth.False, Truth.False, Truth.False, Truth.True, Truth.False]);
    }
}

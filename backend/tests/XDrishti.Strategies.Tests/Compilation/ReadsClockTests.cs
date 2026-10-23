using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>Whether a condition reads the clock (P7-04): a part that does, says nothing about tomorrow from the close of today.</summary>
public sealed class ReadsClockTests
{
    private static CompiledCondition Compile(string source) =>
        ConditionCompiler.Compile(source, new CompileContext(Timeframe.M15)).Condition ?? throw new InvalidOperationException("does not compile: " + source);

    [Theory]
    [InlineData("time >= \"09:45\"", true)]
    [InlineData("close > open and time >= \"09:45\"", true)]
    [InlineData("not (time >= \"09:45\") or close > 0", true)]
    [InlineData("close > sma(20)", false)]
    [InlineData("tf(\"1D\").close > tf(\"1D\").sma(20)", false)]
    public void The_condition_and_its_tree_agree_on_whether_the_clock_is_read(string source, bool expected)
    {
        var condition = Compile(source);

        condition.ReadsClock.ShouldBe(expected);
        BoundTree.ReadsClock(condition.Tree).ShouldBe(expected);
    }
}

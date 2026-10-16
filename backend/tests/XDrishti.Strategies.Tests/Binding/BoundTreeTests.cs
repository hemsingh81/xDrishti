using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>The bound tree that the compiler and the oracle read: its shape follows the grammar's precedence, every node is typed, parameters and constants are numbers, a comparison with the time or the regime has its own form.</summary>
public sealed class BoundTreeTests
{
    private const Timeframe M15 = Timeframe.M15;

    private static BoundFeature Feature(string name, double p1 = 0, int lag = 0, bool reference = true, Timeframe frame = M15, PriceSource source = PriceSource.Close) =>
        new(new FeatureSpec(name, frame, source, p1), lag, BoundType.Number, reference, default);

    private static IBoundExpression Tree(string source, Timeframe frame = M15, IReadOnlyDictionary<string, double>? parameters = null) => Condition(source, frame, parameters).Tree;

    [Fact]
    public void A_comparison_of_a_feature_with_a_number_has_a_feature_a_number_and_the_operator()
    {
        var compare = Tree("rsi(14) > 55").ShouldBeOfType<BoundCompare>();

        compare.Operator.ShouldBe(BinaryOperator.Greater);
        compare.Type.ShouldBe(BoundType.Bool);
        var left = compare.Left.ShouldBeOfType<BoundFeature>();
        left.Spec.ShouldBe(Specs.Rsi(M15));
        left.Lag.ShouldBe(0);
        left.IsReference.ShouldBeFalse("rsi(14) is a call");
        left.Type.ShouldBe(BoundType.Number);
        compare.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(55);
    }

    [Fact]
    public void The_precedence_of_the_grammar_is_the_shape_of_the_tree()
    {
        var tree = Tree("close > 1 or open > 2 and not (high < 3)");

        var or = tree.ShouldBeOfType<BoundLogical>();
        or.Operator.ShouldBe(BinaryOperator.Or);
        or.Left.ShouldBeOfType<BoundCompare>();
        var and = or.Right.ShouldBeOfType<BoundLogical>();
        and.Operator.ShouldBe(BinaryOperator.And);
        var not = and.Right.ShouldBeOfType<BoundNot>();
        not.Operand.ShouldBeOfType<BoundCompare>().Operator.ShouldBe(BinaryOperator.Less);
    }

    [Fact]
    public void Arithmetic_groups_to_the_left_and_binds_tighter_than_a_comparison()
    {
        var compare = Tree("close - open - 1 > high * 2 / 3").ShouldBeOfType<BoundCompare>();

        var minus = compare.Left.ShouldBeOfType<BoundArithmetic>();
        minus.Operator.ShouldBe(BinaryOperator.Subtract);
        minus.Left.ShouldBeOfType<BoundArithmetic>().Operator.ShouldBe(BinaryOperator.Subtract);
        minus.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(1);
        var divide = compare.Right.ShouldBeOfType<BoundArithmetic>();
        divide.Operator.ShouldBe(BinaryOperator.Divide);
        divide.Left.ShouldBeOfType<BoundArithmetic>().Operator.ShouldBe(BinaryOperator.Multiply);
        compare.Type.ShouldBe(BoundType.Bool);
        minus.Type.ShouldBe(BoundType.Number);
    }

    [Fact]
    public void A_subscript_is_the_lag_of_a_feature_that_was_written_as_a_reference()
    {
        var compare = Tree("close[2] > prev_day.high[1]").ShouldBeOfType<BoundCompare>();

        compare.Left.ShouldBeOfType<BoundFeature>().ShouldBe(Feature("close", lag: 2) with { Span = compare.Left.Span });
        compare.Right.ShouldBeOfType<BoundFeature>().Lag.ShouldBe(1);
        compare.Right.ShouldBeOfType<BoundFeature>().Spec.Name.ShouldBe("prev_day_high");
    }

    [Fact]
    public void The_time_is_compared_in_minutes_after_midnight_with_the_time_on_the_left()
    {
        var direct = Tree("time >= \"09:45\"").ShouldBeOfType<BoundTimeCompare>();
        var turned = Tree("\"09:45\" <= time").ShouldBeOfType<BoundTimeCompare>();
        var open = Tree("time == \"09:15\"").ShouldBeOfType<BoundTimeCompare>();
        var close = Tree("time < \"15:30\"").ShouldBeOfType<BoundTimeCompare>();
        var order = Tree("\"10:00\" > time").ShouldBeOfType<BoundTimeCompare>();

        (direct.Operator, direct.MinuteOfDay).ShouldBe((BinaryOperator.GreaterOrEqual, 9 * 60 + 45));
        (turned.Operator, turned.MinuteOfDay).ShouldBe((BinaryOperator.GreaterOrEqual, 585), "`\"09:45\" <= time` is `time >= \"09:45\"`");
        (open.Operator, open.MinuteOfDay).ShouldBe((BinaryOperator.Equal, 555));
        (close.Operator, close.MinuteOfDay).ShouldBe((BinaryOperator.Less, 930));
        (order.Operator, order.MinuteOfDay).ShouldBe((BinaryOperator.Less, 600), "`\"10:00\" > time` is `time < \"10:00\"`");
        direct.Type.ShouldBe(BoundType.Bool);
    }

    [Fact]
    public void A_regime_name_becomes_the_comparison_of_the_regime_feature_with_its_code_and_a_turned_comparison_is_turned_back()
    {
        var named = Tree("regime() == \"RANGE_HIGH_VOL\"").ShouldBeOfType<BoundCompare>();
        var turned = Tree("\"DOWN_LOW_VOL\" != regime()").ShouldBeOfType<BoundCompare>();
        var coded = Tree("3 == regime()").ShouldBeOfType<BoundCompare>();

        named.Left.ShouldBeOfType<BoundFeature>().Spec.ShouldBe(Specs.Of("regime", Timeframe.M1));
        named.Left.Type.ShouldBe(BoundType.Regime);
        (named.Operator, named.Right.ShouldBeOfType<BoundNumber>().Value).ShouldBe((BinaryOperator.Equal, 6));
        turned.Left.ShouldBeOfType<BoundFeature>().Type.ShouldBe(BoundType.Regime);
        (turned.Operator, turned.Right.ShouldBeOfType<BoundNumber>().Value).ShouldBe((BinaryOperator.NotEqual, 7));
        coded.Left.ShouldBeOfType<BoundFeature>().Type.ShouldBe(BoundType.Regime);
        coded.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(3);
    }

    [Theory]
    [InlineData("regime() == 1", BinaryOperator.Equal, 1)]
    [InlineData("regime() != 9", BinaryOperator.NotEqual, 9)]
    [InlineData("regime() == 4 + 2", BinaryOperator.Equal, 6)]
    [InlineData("9 != regime()", BinaryOperator.NotEqual, 9)]
    public void A_regime_code_is_kept_as_written_with_the_regime_on_the_left(string source, BinaryOperator op, int code)
    {
        var compare = Tree(source).ShouldBeOfType<BoundCompare>();

        compare.Left.ShouldBeOfType<BoundFeature>().Type.ShouldBe(BoundType.Regime);
        (compare.Operator, compare.Right.ShouldBeOfType<BoundNumber>().Value).ShouldBe((op, code));
    }

    [Fact]
    public void The_builtins_have_nodes_of_their_own()
    {
        var crossing = Tree("crosses_above(close, vwap)").ShouldBeOfType<BoundCrossing>();
        var below = Tree("crosses_below(close, 100)").ShouldBeOfType<BoundCrossing>();
        var rising = Tree("rising(rsi(14), 3)").ShouldBeOfType<BoundRun>();
        var falling = Tree("falling(close, 255)").ShouldBeOfType<BoundRun>();
        var between = Tree("between(rsi(14), 30, 70)").ShouldBeOfType<BoundBetween>();
        var abs = Tree("abs(close) > min(open, 2)").ShouldBeOfType<BoundCompare>();

        crossing.IsAbove.ShouldBeTrue();
        below.IsAbove.ShouldBeFalse();
        below.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(100);
        (rising.IsRising, rising.Count).ShouldBe((true, 3));
        (falling.IsRising, falling.Count).ShouldBe((false, 255));
        between.Low.ShouldBeOfType<BoundNumber>().Value.ShouldBe(30);
        between.High.ShouldBeOfType<BoundNumber>().Value.ShouldBe(70);
        abs.Left.ShouldBeOfType<BoundAbs>();
        abs.Right.ShouldBeOfType<BoundMinMax>().IsMax.ShouldBeFalse();
        Tree("max(open, 2) > 1").ShouldBeOfType<BoundCompare>().Left.ShouldBeOfType<BoundMinMax>().IsMax.ShouldBeTrue();
    }

    [Fact]
    public void A_minus_sign_and_the_constants_are_nodes_of_numbers()
    {
        var compare = Tree("-close < -3 + TUE").ShouldBeOfType<BoundCompare>();

        compare.Left.ShouldBeOfType<BoundNegate>().Operand.ShouldBeOfType<BoundFeature>();
        var sum = compare.Right.ShouldBeOfType<BoundArithmetic>();
        sum.Left.ShouldBeOfType<BoundNegate>().Operand.ShouldBeOfType<BoundNumber>().Value.ShouldBe(3);
        sum.Right.ShouldBeOfType<BoundNumber>().Value.ShouldBe(2);
    }

    [Fact]
    public void Two_true_false_values_can_be_compared_and_the_keywords_are_nodes()
    {
        var compare = Tree("(close > 1) == true").ShouldBeOfType<BoundCompare>();

        compare.Left.ShouldBeOfType<BoundCompare>();
        compare.Right.ShouldBeOfType<BoundBool>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Every_node_of_the_tree_of_a_valid_condition_has_a_type_and_the_root_is_true_or_false()
    {
        var generator = new ValidConditionGenerator(31, M15);
        for (var i = 0; i < 400; i++)
        {
            var generated = generator.Next();
            var tree = Tree(generated.Source, M15, ValidConditionGenerator.Parameters);

            tree.Type.ShouldBe(BoundType.Bool, generated.Source);
            Visit(tree, node =>
            {
                node.Type.ShouldBeOneOf([BoundType.Number, BoundType.Bool, BoundType.Regime], generated.Source + " -> " + node);
                node.ShouldNotBeOfType<BoundInvalid>();
                node.ShouldNotBeOfType<BoundText>();
                node.ShouldNotBeOfType<BoundTimeKeyword>();
            });
        }
    }

    [Fact]
    public void The_features_of_the_tree_in_order_are_the_features_of_the_plan_with_repeats()
    {
        var plan = Condition("close > open and open > close[1] and rsi(14) > 55");
        var leaves = new List<BoundFeature>();

        BoundTree.CollectFeatures(plan.Tree, leaves);

        leaves.Select(l => l.Spec.Name).ShouldBe(["close", "open", "open", "close", "rsi"]);
        leaves.Select(l => l.Lag).ShouldBe([0, 0, 0, 1, 0]);
        plan.Features.Select(f => f.Name).ShouldBe(["close", "open", "rsi"]);
    }

    [Fact]
    public void The_spans_of_the_nodes_are_the_text_they_were_read_from()
    {
        var source = "rsi(14) > 55 and crosses_above(close, vwap)";
        var and = Condition(source).Tree.ShouldBeOfType<BoundLogical>();

        source[and.Left.Span.Start..and.Left.Span.End].ShouldBe("rsi(14) > 55");
        source[and.Right.Span.Start..and.Right.Span.End].ShouldBe("crosses_above(close, vwap)");
        var crossing = and.Right.ShouldBeOfType<BoundCrossing>();
        source[crossing.Left.Span.Start..crossing.Left.Span.End].ShouldBe("close");
        source[crossing.Right.Span.Start..crossing.Right.Span.End].ShouldBe("vwap");
    }

    private static void Visit(IBoundExpression node, Action<IBoundExpression> check)
    {
        check(node);
        foreach (var child in BoundTree.Children(node))
        {
            Visit(child, check);
        }
    }
}

using System.Collections.Immutable;
using XDrishti.Strategies.Syntax;
using static XDrishti.Strategies.Tests.Support.Ast;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>Two nodes are equal when they have the same shape and values; where they were read from is not part of it.</summary>
public sealed class SyntaxNodeEqualityTests
{
    [Fact]
    public void The_same_condition_written_with_different_white_space_is_an_equal_tree_with_equal_hash_codes()
    {
        var compact = ConditionParser.Parse("rsi(14)>tf(\"1h\").close[1]+prev_day.high").Tree!;
        var spread = ConditionParser.Parse("  rsi( 14 )\n  >\n tf(\"1h\") . close [ 1 ] + prev_day . high ").Tree!;

        spread.Span.ShouldNotBe(compact.Span);
        spread.ShouldBe(compact);
        spread.GetHashCode().ShouldBe(compact.GetHashCode());
        (spread == compact).ShouldBeTrue();
    }

    [Fact]
    public void Parentheses_that_change_nothing_are_not_part_of_the_tree()
    {
        ConditionParser.Parse("((a)) + (b)").Tree.ShouldBe(ConditionParser.Parse("a + b").Tree);
    }

    [Fact]
    public void A_different_value_name_operator_or_shape_makes_a_different_tree()
    {
        var baseline = Gt(C("rsi", N(14)), N(55));
        Expression[] different =
        [
            Gt(C("rsi", N(15)), N(55)),
            Gt(C("rsi", N(14)), N(56)),
            Gt(C("ema", N(14)), N(55)),
            Lt(C("rsi", N(14)), N(55)),
            Gt(C("rsi", N(14), N(1)), N(55)),
            Gt(C("rsi"), N(55)),
            Gt(R("rsi"), N(55)),
            Gt(C("rsi", S("14")), N(55)),
            Gt(C("rsi", N(14)), N(55)) is Binary b ? b.Left : N(0),
            Not(baseline),
            TfC("1h", "rsi", N(14)),
        ];

        foreach (var other in different)
        {
            other.ShouldNotBe(baseline);
        }
    }

    [Fact]
    public void A_scope_a_timeframe_and_no_qualifier_are_three_different_references()
    {
        R("a", "b").ShouldNotBe(Tf("a", "b"));
        R("a", "b").ShouldNotBe(R("b"));
        R("a", "b").ShouldNotBe(R("b", "a"));
        Tf("1h", "close").ShouldNotBe(Tf("1D", "close"));
        Tf("1h", "close").ShouldNotBe(R("close"));
        R("close").ShouldBe(R("close"));
    }

    [Fact]
    public void A_subscript_is_equal_to_another_with_the_same_target_and_lag()
    {
        Idx(R("close"), 1).ShouldBe(Idx(R("close"), 1));
        Idx(R("close"), 1).ShouldNotBe(Idx(R("close"), 2));
        Idx(R("close"), 1).ShouldNotBe(Idx(R("open"), 1));
        ((Expression)Idx(R("close"), 1)).ShouldNotBe(R("close"));
    }

    [Fact]
    public void Named_and_positional_arguments_and_their_order_matter()
    {
        C("ema", N(20), Named("source", "high")).ShouldBe(C("ema", N(20), Named("source", "high")));
        C("ema", N(20), Named("source", "high")).ShouldNotBe(C("ema", N(20), Named("source", "low")));
        C("ema", N(20), Named("source", "high")).ShouldNotBe(C("ema", N(20), Named("src", "high")));
        C("ema", N(20), Named("source", "high")).ShouldNotBe(C("ema", Named("source", "high"), N(20)));
        C("ema", N(20), Named("source", "high")).ShouldNotBe(C("ema", N(20), R("high")));
    }

    [Fact]
    public void An_argument_list_that_was_never_set_equals_an_empty_one()
    {
        var unset = new Call(Id("regime"), default, default);
        var empty = new Call(Id("regime"), ImmutableArray<Argument>.Empty, default);

        unset.ShouldBe(empty);
        unset.GetHashCode().ShouldBe(empty.GetHashCode());
    }

    [Fact]
    public void Numbers_strings_and_booleans_compare_by_value()
    {
        N(0.5).ShouldBe(N(0.5));
        N(0.5).ShouldNotBe(N(0.25));
        S("a").ShouldBe(S("a"));
        S("a").ShouldNotBe(S("A"));
        B(true).ShouldNotBe(B(false));
        ((Expression)N(1)).ShouldNotBe(B(true));
        Id("x").ShouldNotBe(Id("X"));
    }

    [Fact]
    public void Equal_trees_have_equal_hash_codes_for_every_valid_source()
    {
        foreach (var (source, expected) in ValidSources.Cases)
        {
            ConditionParser.Parse(source).Tree!.GetHashCode().ShouldBe(expected.GetHashCode(), source);
        }
    }

    [Fact]
    public void A_node_with_a_new_span_is_still_the_same_node()
    {
        var node = ConditionParser.Parse("a + b").Tree!;

        (node with { Span = new SourceSpan(5, 9, 3, 1) }).ShouldBe(node);
    }
}

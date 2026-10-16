using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The printer decides what text the compiler is given for a model, so it is pinned on its own: brackets only where the grammar's precedence and associativity need them (and everywhere in the other mode), the three
/// spacings, the mirrored forms of <c>time</c> and <c>regime()</c>, subscripts and <c>tf(…)</c>.
/// </summary>
public sealed class OraclePrinterTests
{
    private static Const V(string name) => new(name, 1);

    private static readonly Const A = V("a");

    private static readonly Const B = V("b");

    private static readonly Const C = V("c");

    private static readonly Const D = V("d");

    private static string Minimal(Node node) => Printer.Print(node, Parens.Minimal);

    private static string Always(Node node) => Printer.Print(node, Parens.Always);

    [Fact]
    public void Left_associative_operators_print_without_brackets_on_the_left_and_with_them_on_the_right()
    {
        Minimal(new Arith('-', new Arith('-', A, B), C)).ShouldBe("a - b - c");
        Minimal(new Arith('-', A, new Arith('-', B, C))).ShouldBe("a - (b - c)");
        Minimal(new Arith('/', new Arith('/', A, B), C)).ShouldBe("a / b / c");
        Minimal(new Arith('/', A, new Arith('*', B, C))).ShouldBe("a / (b * c)");
    }

    [Fact]
    public void A_tighter_operator_needs_no_brackets_and_a_looser_one_inside_it_does()
    {
        Minimal(new Arith('+', A, new Arith('*', B, C))).ShouldBe("a + b * c");
        Minimal(new Arith('*', new Arith('+', A, B), C)).ShouldBe("(a + b) * c");
        Minimal(new Arith('*', A, new Arith('+', B, C))).ShouldBe("a * (b + c)");
    }

    [Fact]
    public void Unary_minus_binds_tighter_than_the_product_and_does_not_touch_another_minus()
    {
        Minimal(new Arith('*', new Neg(A), B)).ShouldBe("-a * b");
        Minimal(new Neg(new Arith('*', A, B))).ShouldBe("-(a * b)");
        Minimal(new Neg(new Neg(A))).ShouldBe("- -a");
        Minimal(new Arith('-', A, new Neg(B))).ShouldBe("a - -b");
        Printer.Print(new Arith('-', A, new Neg(B)), Parens.Minimal, Spacing.Tight).ShouldBe("a - -b");
    }

    [Fact]
    public void Or_is_looser_than_and_and_not_is_tighter_than_both()
    {
        Minimal(new Logic(false, A, new Logic(true, B, C))).ShouldBe("a or b and c");
        Minimal(new Logic(true, new Logic(false, A, B), C)).ShouldBe("(a or b) and c");
        Minimal(new Logic(true, new Logic(true, A, B), C)).ShouldBe("a and b and c");
        Minimal(new Logic(true, A, new Logic(true, B, C))).ShouldBe("a and (b and c)");
        Minimal(new Logic(true, new Not(A), B)).ShouldBe("not a and b");
        Minimal(new Not(new Logic(true, A, B))).ShouldBe("not (a and b)");
        Minimal(new Not(new Not(A))).ShouldBe("not not a");
    }

    [Fact]
    public void A_comparison_is_not_chained_so_a_comparison_inside_a_comparison_is_bracketed()
    {
        Minimal(new Cmp("<", new Arith('+', A, B), C)).ShouldBe("a + b < c");
        Minimal(new Cmp("==", new Cmp("<", A, B), new Cmp(">", C, D))).ShouldBe("(a < b) == (c > d)");
        Minimal(new Not(new Cmp("<", A, B))).ShouldBe("not a < b");
        Minimal(new Cmp("!=", new Logic(true, A, B), new BoolLit(true))).ShouldBe("(a and b) != true");
    }

    [Fact]
    public void In_the_always_mode_every_compound_part_is_in_brackets_and_the_whole_is_not()
    {
        Always(new Logic(true, new Cmp("<", A, B), new Not(C))).ShouldBe("(a < b) and (not c)");
        Always(new Arith('+', A, new Arith('*', B, C))).ShouldBe("a + (b * c)");
        Always(new Neg(new Arith('*', A, B))).ShouldBe("-(a * b)");
        Always(new Arith('*', new Neg(A), B)).ShouldBe("(-a) * b");
    }

    [Fact]
    public void Calls_are_primary_and_their_arguments_are_whole_expressions()
    {
        Minimal(new Arith('*', new MinMax(true, new Arith('+', A, B), C), D)).ShouldBe("max(a + b, c) * d");
        Minimal(new Abs(new Logic(true, A, B))).ShouldBe("abs(a and b)");
        Minimal(new Crosses(true, new Arith('-', A, B), C)).ShouldBe("crosses_above(a - b, c)");
        Minimal(new Crosses(false, A, B)).ShouldBe("crosses_below(a, b)");
        Minimal(new Streak(true, A, 3)).ShouldBe("rising(a, 3)");
        Minimal(new Streak(false, A, 12)).ShouldBe("falling(a, 12)");
        Minimal(new Between(A, B, new Arith('+', C, D))).ShouldBe("between(a, b, c + d)");
    }

    [Fact]
    public void A_feature_is_written_with_its_subscript_and_with_tf_when_qualified()
    {
        var close = new Feature("close", "close", true, new FeatureSpec("close"));
        var dotted = new Feature("prev_day.high", "prev_day_high", true, new FeatureSpec("prev_day_high"));

        Minimal(new Leaf(close, Timeframe.M15, false, 0)).ShouldBe("close");
        Minimal(new Leaf(close, Timeframe.M15, false, 3)).ShouldBe("close[3]");
        Minimal(new Leaf(close, Timeframe.H1, true, 0)).ShouldBe("tf(\"1h\").close");
        Minimal(new Leaf(close, Timeframe.Mo1, true, 2)).ShouldBe("tf(\"1M\").close[2]");
        Minimal(new Leaf(dotted, Timeframe.M15, false, 1)).ShouldBe("prev_day.high[1]");
        Minimal(new Leaf(dotted, Timeframe.D1, true, 0)).ShouldBe("tf(\"1D\").prev_day_high");
    }

    [Fact]
    public void Time_and_the_regime_are_written_in_both_orders()
    {
        Minimal(new TimeCmp(">=", 585, "09:45", false)).ShouldBe("time >= \"09:45\"");
        Minimal(new TimeCmp(">=", 585, "09:45", true)).ShouldBe("\"09:45\" <= time");
        Minimal(new TimeCmp("<", 585, "09:45", true)).ShouldBe("\"09:45\" > time");
        Minimal(new TimeCmp("==", 585, "09:45", true)).ShouldBe("\"09:45\" == time");
        var regime = new Leaf(new Feature("regime()", "regime()", false, new FeatureSpec("regime")), Timeframe.M1, false, 0);
        Minimal(new RegimeCmp(true, 1, "UP_LOW_VOL", false, regime)).ShouldBe("regime() == \"UP_LOW_VOL\"");
        Minimal(new RegimeCmp(false, 1, "UP_LOW_VOL", true, regime)).ShouldBe("\"UP_LOW_VOL\" != regime()");
        Minimal(new RegimeCmp(true, 7, null, false, regime)).ShouldBe("regime() == 7");
    }

    [Fact]
    public void The_spacings_change_only_the_whitespace()
    {
        var node = new Logic(true, new Cmp(">", A, new Arith('+', B, C)), new Crosses(true, A, B));

        Printer.Print(node, Parens.Minimal, Spacing.Normal).ShouldBe("a > b + c and crosses_above(a, b)");
        Printer.Print(node, Parens.Minimal, Spacing.Tight).ShouldBe("a>b+c and crosses_above(a,b)");
        Printer.Print(node, Parens.Minimal, Spacing.Wide).ShouldBe("a\t>\tb\t+\tc\nand\ncrosses_above(a ,\tb)");
    }
}

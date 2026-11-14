using XDrishti.Strategies.Lint;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>
/// The types of the spec (<i>Types</i>): <c>and or not</c> take true/false values; arithmetic and ordering take numbers; <c>==</c> and <c>!=</c> take two numbers, two true/false values, or <c>time</c> with a time text;
/// anything else is a type error. Every pair of the five kinds of operand is tried with every operator; the rule that decides is written out once, below, from the spec.
/// </summary>
public sealed class TypeRulesTests
{
    public enum Kind
    {
        Number,
        Constant,
        Bool,
        Time,
        TimeText,
    }

    private static readonly Dictionary<Kind, string> Snippets = new()
    {
        [Kind.Number] = "close",
        [Kind.Constant] = "3",
        [Kind.Bool] = "(close > 1)",
        [Kind.Time] = "time",
        [Kind.TimeText] = "\"09:45\"",
    };

    public static TheoryData<string, Kind, Kind> Pairs { get; } =
        [.. new[] { "and", "or", "+", "-", "*", "/", "<", "<=", ">", ">=", "==", "!=" }.SelectMany(op => Enum.GetValues<Kind>().SelectMany(l => Enum.GetValues<Kind>().Select(r => (op, l, r))))];

    public static TheoryData<Kind> Kinds { get; } = [.. Enum.GetValues<Kind>()];

    private static bool Numeric(Kind kind) => kind is Kind.Number or Kind.Constant;

    /// <summary>Whether the spec accepts the operator between the two kinds.</summary>
    private static bool Accepts(string op, Kind left, Kind right) => op switch
    {
        "and" or "or" => left == Kind.Bool && right == Kind.Bool,
        "+" or "-" or "*" or "/" => Numeric(left) && Numeric(right),
        "<" or "<=" or ">" or ">=" => (Numeric(left) && Numeric(right)) || (left == Kind.Time && right == Kind.TimeText) || (left == Kind.TimeText && right == Kind.Time),
        _ => (Numeric(left) && Numeric(right))
            || (left == Kind.Bool && right == Kind.Bool)
            || (left == Kind.Time && right == Kind.TimeText) || (left == Kind.TimeText && right == Kind.Time),
    };

    private static string Written(string op, Kind left, Kind right) =>
        op is "+" or "-" or "*" or "/" ? $"({Snippets[left]} {op} {Snippets[right]}) > 0" : $"{Snippets[left]} {op} {Snippets[right]}";

    [Fact]
    public void The_matrix_has_300_cases()
    {
        Pairs.Count.ShouldBe(12 * 5 * 5);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void An_operator_accepts_exactly_the_pairs_of_types_the_spec_allows(string op, Kind left, Kind right)
    {
        var source = Written(op, left, right);

        var result = Of(source);

        if (Accepts(op, left, right))
        {
            result.IsSuccess.ShouldBeTrue(source + " -> " + Describe(result));
        }
        else
        {
            result.IsSuccess.ShouldBeFalse(source);
            result.Errors.ShouldNotBeEmpty(source);
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_pair_of_the_wrong_types_is_a_type_mismatch_(string op, Kind left, Kind right)
    {
        if (Accepts(op, left, right))
        {
            return;
        }

        var source = Written(op, left, right);
        var errors = Of(source).Errors.ToList();

        errors.ShouldNotBeEmpty(source);
        errors[0].Code.ShouldBe(LintCodes.TypeMismatch, source);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_comparison_of_the_wrong_types_names_both_sides_in_its_message(string op, Kind left, Kind right)
    {
        if (Accepts(op, left, right) || op is "and" or "or" or "+" or "-" or "*" or "/")
        {
            return;
        }

        var error = Of(Written(op, left, right)).Errors.First();

        error.Message.ShouldContain("`" + op + "`", Case.Sensitive);
        error.Message.ShouldContain("`" + Snippets[left] + "`", Case.Sensitive);
        error.Message.ShouldContain("`" + Snippets[right] + "`", Case.Sensitive);
    }

    /// <summary>How a message calls the type an operand has.</summary>
    private static string TypeWords(Kind kind) => kind switch
    {
        Kind.Number or Kind.Constant => "a number",
        Kind.Bool => "a true/false value",
        Kind.Time => "the time of day",
        _ => "a text",
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_wrong_operand_of_a_logical_or_arithmetic_operator_is_reported_on_itself_with_the_type_it_needs_and_the_one_it_has_and_both_when_both_are_wrong(string op, Kind left, Kind right)
    {
        if (op is not ("and" or "or" or "+" or "-" or "*" or "/"))
        {
            return;
        }

        var logical = op is "and" or "or";
        var firstStart = logical ? 0 : 1; // an arithmetic operand is written inside "( … ) > 0"
        var secondStart = firstStart + Snippets[left].Length + op.Length + 2;
        var source = Written(op, left, right);
        (string Side, Kind Kind, int Start)[] operands = [("left", left, firstStart), ("right", right, secondStart)];
        var wrong = operands.Where(o => logical ? o.Kind != Kind.Bool : !Numeric(o.Kind)).ToList();

        var errors = Of(source).Errors.ToList();

        errors.Count.ShouldBe(wrong.Count, source + " -> " + string.Join(" | ", errors.Select(e => e.Message)));
        foreach (var (side, kind, start) in wrong)
        {
            var error = errors.Single(e => e.Span.Start == start);
            error.Code.ShouldBe(LintCodes.TypeMismatch, source);
            error.Span.End.ShouldBe(start + Snippets[kind].Length, source + ": the span is the operand and nothing more");
            error.Message.ShouldStartWith($"The {side} side of `{op}` needs {(logical ? "a true/false value" : "a number")}, but `{Snippets[kind]}` is {TypeWords(kind)}.", Case.Sensitive);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Only_a_true_false_value_is_a_condition(Kind kind)
    {
        var result = Of(Snippets[kind]);

        result.IsSuccess.ShouldBe(kind == Kind.Bool, Snippets[kind]);
        if (kind != Kind.Bool)
        {
            result.Errors.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.TypeMismatch);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Not_takes_a_true_false_value_and_minus_takes_a_number(Kind kind)
    {
        Of("not " + Snippets[kind]).IsSuccess.ShouldBe(kind == Kind.Bool, "not " + Snippets[kind]);
        Of("-" + Snippets[kind] + " > 0").IsSuccess.ShouldBe(Numeric(kind), "-" + Snippets[kind]);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_arguments_of_the_functions_are_numbers(Kind kind)
    {
        var x = Snippets[kind];
        var shapes = new[] { $"crosses_above({x}, close)", $"crosses_below(close, {x})", $"rising({x}, 2)", $"falling({x}, 2)", $"between({x}, 1, 2)", $"between(close, {x}, 2)", $"between(close, 1, {x})", $"abs({x}) > 0", $"min({x}, 1) > 0", $"max(1, {x}) > 0" };

        foreach (var shape in shapes)
        {
            Of(shape).IsSuccess.ShouldBe(Numeric(kind), shape);
        }
    }

    /// <summary>A sum of ones, exactly <paramref name="length"/> characters long (the last number gets the extra digits).</summary>
    private static string Sum(int length)
    {
        var text = "1";
        while (text.Length + 4 <= length)
        {
            text += " + 1";
        }

        return text + new string('1', length - text.Length);
    }

    [Fact]
    public void A_quoted_text_of_60_characters_is_shown_whole_and_one_of_61_is_shortened_to_57_and_three_dots()
    {
        var sixty = Sum(60);
        var sixtyOne = Sum(61);

        var whole = OnlyError(sixty);
        var shortened = OnlyError(sixtyOne);

        sixty.Length.ShouldBe(60);
        sixtyOne.Length.ShouldBe(61);
        whole.Code.ShouldBe(LintCodes.TypeMismatch);
        whole.Message.ShouldContain("`" + sixty + "`", Case.Sensitive);
        shortened.Message.ShouldContain("`" + sixtyOne[..57] + "...`", Case.Sensitive);
        shortened.Message.ShouldNotContain(sixtyOne, Case.Sensitive);
        shortened.Span.End.ShouldBe(61, "the span is still the whole text; only the quotation is shortened");
    }
}

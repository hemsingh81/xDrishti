using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>Outside strings only ASCII is part of the language: look-alikes and every other character are refused with a problem that names the code point, never read as something else.</summary>
public sealed class ParserUnicodeTests
{
    [Theory]
    [InlineData("a ＞ b", 2, 1, "U+FF1E")]
    [InlineData("a\u00A0> b", 1, 1, "U+00A0")]
    [InlineData("a > ٣", 4, 1, "U+0663")]
    [InlineData("α > 1", 0, 1, "U+03B1")]
    [InlineData("a > 1 ＋ 2", 6, 1, "U+FF0B")]
    [InlineData("a > 1 ：", 6, 1, "U+FF1A")]
    [InlineData("😀 > 1", 0, 2, "U+1F600")]
    [InlineData("\uFEFFa > 1", 0, 1, "U+FEFF")]
    [InlineData("a\u202E > 1", 1, 1, "U+202E")]
    [InlineData("a\u2003> 1", 1, 1, "U+2003")]
    [InlineData("a\u3000> 1", 1, 1, "U+3000")]
    [InlineData("a > 1\u0000", 5, 1, "U+0000")]
    [InlineData("a\f> 1", 1, 1, "U+000C")]
    [InlineData("a\v> 1", 1, 1, "U+000B")]
    [InlineData("café > 1", 3, 1, "U+00E9")]
    [InlineData("a > 1 #", 6, 1, "U+0023")]
    public void An_unknown_character_is_one_problem_that_names_its_code_point_and_covers_the_whole_character(string source, int start, int length, string codePoint)
    {
        var result = ConditionParser.Parse(source);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        problem.Span.Start.ShouldBe(start);
        problem.Span.Length.ShouldBe(length);
        problem.Message.ShouldContain(codePoint);
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void A_lone_surrogate_is_an_unknown_character_of_its_own()
    {
        // Not a theory row: the test framework cannot carry an unpaired surrogate through its data serialisation.
        var high = ConditionParser.Parse("a > \uD800").Problems.ShouldHaveSingleItem();
        var low = ConditionParser.Parse("a > \uDC00").Problems.ShouldHaveSingleItem();
        var reversed = ConditionParser.Parse("a > \uDC00\uD800").Problems;

        high.Span.ShouldBe(new SourceSpan(4, 5, 1, 5));
        high.Message.ShouldContain("U+D800");
        low.Span.ShouldBe(new SourceSpan(4, 5, 1, 5));
        low.Message.ShouldContain("U+DC00");
        reversed.Select(p => p.Span.Length).ShouldBe([1, 1]);
    }

    [Fact]
    public void A_non_ascii_digit_is_not_a_number()
    {
        ConditionParser.Parse("٣").Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        ConditionParser.Parse("１２").Problems.Select(p => p.Code).ShouldBe([SyntaxCodes.UnknownCharacter, SyntaxCodes.UnknownCharacter]);
    }

    [Fact]
    public void A_non_ascii_letter_cannot_be_part_of_a_name()
    {
        var problem = ConditionParser.Parse("rsé(14) > 1").Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        problem.Span.ShouldBe(new SourceSpan(2, 3, 1, 3));
    }

    [Fact]
    public void A_character_that_looks_like_an_operator_comes_with_the_operator_as_the_suggestion()
    {
        ConditionParser.Parse("a ＞ b").Problems[0].Suggestion.ShouldBe(">");
        ConditionParser.Parse("a ＜ b").Problems[0].Suggestion.ShouldBe("<");
        ConditionParser.Parse("a ＝＝ b").Problems[0].Suggestion.ShouldBe("=");
        ConditionParser.Parse("a ≤ b").Problems[0].Suggestion.ShouldBe("<=");
        ConditionParser.Parse("a ≠ b").Problems[0].Suggestion.ShouldBe("!=");
        ConditionParser.Parse("a × b").Problems[0].Suggestion.ShouldBe("*");
        ConditionParser.Parse("a ÷ b").Problems[0].Suggestion.ShouldBe("/");
        ConditionParser.Parse("a – b").Problems[0].Suggestion.ShouldBe("-");
        ConditionParser.Parse("a\u00A0< b").Problems[0].Suggestion.ShouldBe(" ");
    }

    [Fact]
    public void A_look_alike_of_a_token_is_one_problem_and_the_rest_of_the_condition_is_read_as_if_it_had_been_the_token()
    {
        // If the full-width greater-than were not read as ">", the second problem would be "a b" side by side.
        ConditionParser.Parse("rsi(14) ＞ 55 and close > vwap").Problems.ShouldHaveSingleItem();
        ConditionParser.Parse("a && b").Problems.ShouldHaveSingleItem();
        ConditionParser.Parse("a || b && c").Problems.Count.ShouldBe(2);
        ConditionParser.Parse("a\u00A0\u00A0>\u00A0b").Problems.Count.ShouldBe(3);
        ConditionParser.Parse("（a + b） ＊ c").Problems.Count.ShouldBe(3);
    }

    [Fact]
    public void An_unknown_character_message_shows_the_character_when_it_can_be_shown_and_only_the_code_point_when_not()
    {
        ConditionParser.Parse("a ＞ b").Problems[0].Message.ShouldBe("Unknown character '＞' (U+FF1E); it is not part of the rules language.");
        ConditionParser.Parse("a > b 😀").Problems[0].Message.ShouldBe("Unknown character '😀' (U+1F600); it is not part of the rules language.");
        ConditionParser.Parse("a\u00A0> b").Problems[0].Message.ShouldBe("Unknown character U+00A0; it is not part of the rules language.");
        ConditionParser.Parse("a\u202E> b").Problems[0].Message.ShouldBe("Unknown character U+202E; it is not part of the rules language.");
        ConditionParser.Parse("a && b").Problems[0].Message.ShouldBe("Unknown character '&&' (U+0026 U+0026); it is not part of the rules language.");
        ConditionParser.Parse("a > \uD800").Problems[0].Message.ShouldBe("Unknown character U+D800; it is not part of the rules language.");
    }

    // The space look-alikes and the zero-width characters, by code point (the source writes them as \u escapes; these lists are the contract).
    public static TheoryData<int> SpaceLookAlikes { get; } = [0x00A0, 0x1680, .. Enumerable.Range(0x2000, 11), 0x202F, 0x205F, 0x3000];

    public static TheoryData<int> ZeroWidthCharacters { get; } = [0x200B, 0x200C, 0x200D, 0x2060, 0xFEFF];

    /// <summary>Characters next to those two lists that are not white space and must not be dropped: line and paragraph separators, direction marks, a soft hyphen, the Mongolian vowel separator, neighbours of the ranges.</summary>
    public static TheoryData<int> NotDropped { get; } = [0x00AD, 0x167F, 0x1681, 0x180E, 0x1FFF, 0x200E, 0x200F, 0x2028, 0x2029, 0x202E, 0x2030, 0x205E, 0x2061, 0x2FFF, 0x3001, 0xFEFE, 0xFF00];

    [Theory]
    [MemberData(nameof(SpaceLookAlikes))]
    public void A_space_look_alike_is_dropped_as_white_space_and_reported_once_with_a_plain_space_as_the_suggestion(int codePoint)
    {
        var text = char.ConvertFromUtf32(codePoint);

        var reading = UnknownCharacters.Read(text);
        var problem = ConditionParser.Parse("a" + text + "> b").Problems.ShouldHaveSingleItem();

        (reading.Drop, reading.Stands, reading.Suggestion).ShouldBe((true, Tk.None, " "));
        problem.Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        problem.Suggestion.ShouldBe(" ");
        problem.Message.ShouldContain("U+" + codePoint.ToString("X4"), Case.Sensitive);
        problem.Span.ShouldBe(new SourceSpan(1, 2, 1, 2));
    }

    [Theory]
    [MemberData(nameof(ZeroWidthCharacters))]
    public void A_zero_width_character_is_dropped_and_reported_once_with_no_suggestion(int codePoint)
    {
        var text = char.ConvertFromUtf32(codePoint);

        var reading = UnknownCharacters.Read(text);
        var problem = ConditionParser.Parse("a" + text + " > b").Problems.ShouldHaveSingleItem();

        (reading.Drop, reading.Stands, reading.Suggestion).ShouldBe((true, Tk.None, null));
        problem.Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        problem.Suggestion.ShouldBeNull();
        problem.Message.ShouldContain("U+" + codePoint.ToString("X4"), Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(NotDropped))]
    public void A_character_next_to_those_lists_is_not_dropped(int codePoint)
    {
        var reading = UnknownCharacters.Read(char.ConvertFromUtf32(codePoint));

        reading.Drop.ShouldBeFalse("U+" + codePoint.ToString("X4"));
        reading.Stands.ShouldBe(Tk.None);
    }

    [Fact]
    public void A_string_in_other_quotes_is_one_problem_with_the_double_quoted_string_as_the_suggestion()
    {
        var single = ConditionParser.Parse("regime() == 'UP_LOW_VOL'").Problems.ShouldHaveSingleItem();
        var typographic = ConditionParser.Parse("time >= “09:45”").Problems.ShouldHaveSingleItem();

        single.Suggestion.ShouldBe("\"UP_LOW_VOL\"");
        single.Span.ShouldBe(new SourceSpan(12, 24, 1, 13));
        single.Message.ShouldBe("Unknown character ''' (U+0027): strings are written with straight double quotes, like \"UP_LOW_VOL\".");
        typographic.Suggestion.ShouldBe("\"09:45\"");
        typographic.Message.ShouldContain("U+201C");
    }

    [Fact]
    public void A_single_quote_that_is_never_closed_is_an_unknown_character()
    {
        var problem = ConditionParser.Parse("a == 'abc").Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe(SyntaxCodes.UnknownCharacter);
        problem.Span.ShouldBe(new SourceSpan(5, 6, 1, 6));
        problem.Message.ShouldContain("U+0027");
    }

    [Fact]
    public void Unicode_inside_a_string_is_part_of_the_string()
    {
        var result = ConditionParser.Parse("regime() == \"भारत 😀 ＞ é\"");

        result.Problems.ShouldBeEmpty();
        result.Tree.ShouldBeOfType<Binary>().Right.ShouldBeOfType<StringLiteral>().Value.ShouldBe("भारत 😀 ＞ é");
        ExpressionPrinter.Print(result.Tree).ShouldBe("regime() == \"भारत 😀 ＞ é\"");
    }

    [Fact]
    public void Upper_case_letters_are_part_of_names_because_the_day_constants_are_written_that_way()
    {
        var result = ConditionParser.Parse("day_of_week == MON and RSI(14) > 1");

        result.Problems.ShouldBeEmpty();
        result.Tree.ShouldBeOfType<Binary>().Right.ShouldBeOfType<Binary>().Left.ShouldBeOfType<Call>().Name.Text.ShouldBe("RSI");
    }

    [Fact]
    public void Keywords_are_whole_words_only()
    {
        var result = ConditionParser.Parse("order > android and nothing < true_ or android1 == orx");

        result.Problems.ShouldBeEmpty();
        ExpressionPrinter.Print(result.Tree!).ShouldBe("order > android and nothing < true_ or android1 == orx");
    }

    [Fact]
    public void Keywords_are_case_sensitive()
    {
        var result = ConditionParser.Parse("a AND b");

        // "AND" is a name, so this is "a" next to "AND" and then "b" next to it.
        result.Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.MissingOperator);
    }
}

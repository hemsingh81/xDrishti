namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The stable codes of the problems the parser reports (<c>strategy.syntax.*</c>). Each has a span with a line and column. A program switches on the code, a person reads the message.
/// </summary>
public static class SyntaxCodes
{
    /// <summary>The text is empty or only whitespace.</summary>
    public const string Empty = "strategy.syntax.empty";

    /// <summary>The text is longer than <see cref="ConditionParser.MaxLength"/> characters; the span is the part beyond the limit.</summary>
    public const string TooLong = "strategy.syntax.too_long";

    /// <summary>
    /// The condition nests deeper than <see cref="ConditionParser.MaxNesting"/> levels: either more brackets and prefix operators open at once (the span is the token that goes over), or a syntax tree that is too deep
    /// (every call, prefix operator, subscript and each operator of a chain such as <c>a and b and …</c> is a level; the span is the whole condition).
    /// </summary>
    public const string TooDeep = "strategy.syntax.too_deep";

    /// <summary>A character that is not part of the language (including look-alikes such as a full-width <c>＞</c> or a no-break space); the message names the code point.</summary>
    public const string UnknownCharacter = "strategy.syntax.unknown_character";

    /// <summary>A double quote with no closing quote on its line; the span runs to the end of the line.</summary>
    public const string UnterminatedString = "strategy.syntax.unterminated_string";

    /// <summary>A number glued to letters, digits or underscores (<c>55abc</c>, <c>5m</c>, <c>1E5</c>); the span is the whole word.</summary>
    public const string NumberFollowedByIdentifier = "strategy.syntax.number_followed_by_identifier";

    /// <summary>A number the language cannot hold (<c>1e400</c>, <c>1e-400</c>, a subscript above the range of an integer).</summary>
    public const string NumberOutOfRange = "strategy.syntax.number_out_of_range";

    /// <summary>The condition stops where more is needed (after an operator, a comma or a dot); the span is the empty position at the end.</summary>
    public const string UnexpectedEnd = "strategy.syntax.unexpected_end";

    /// <summary>A token that cannot be here and fits none of the more specific codes.</summary>
    public const string UnexpectedToken = "strategy.syntax.unexpected_token";

    /// <summary>An operator, a closing bracket or a comma where a value is needed (<c>a + * b</c>, <c>(a +)</c>, <c>&gt; 55</c>); the span is the token that cannot be there.</summary>
    public const string MissingOperand = "strategy.syntax.missing_operand";

    /// <summary>Two values side by side with nothing between them (<c>a b</c>, <c>rsi(14) 55</c>); the span is the second one.</summary>
    public const string MissingOperator = "strategy.syntax.missing_operator";

    /// <summary>A second comparison after a first (<c>a &lt; b &lt; c</c>); the span is the second operator. Join comparisons with <c>and</c>.</summary>
    public const string ChainedComparison = "strategy.syntax.chained_comparison";

    /// <summary>An opening bracket that is never closed; the span is the bracket.</summary>
    public const string UnclosedBracket = "strategy.syntax.unclosed_bracket";

    /// <summary>A closing bracket with nothing to close, or of the wrong kind; the span is the bracket.</summary>
    public const string UnmatchedClosingBracket = "strategy.syntax.unmatched_closing_bracket";

    /// <summary>A comma with no argument before it (<c>f(,1)</c>, <c>f(1,,2)</c>); the span is the comma.</summary>
    public const string MissingArgument = "strategy.syntax.missing_argument";

    /// <summary>A comma with no argument after it (<c>f(1,)</c>); the span is the comma.</summary>
    public const string TrailingComma = "strategy.syntax.trailing_comma";

    /// <summary>A subscript after something that is not a reference (<c>rsi(14)[1]</c>, <c>(a)[1]</c>, <c>x[1][2]</c>); the span is the opening bracket of the subscript.</summary>
    public const string SubscriptOnNonReference = "strategy.syntax.subscript_on_non_reference";

    /// <summary>A subscript that is not a whole number in digits (<c>x[]</c>, <c>x[a]</c>, <c>x[1.5]</c>); the span is the offending token.</summary>
    public const string BadSubscript = "strategy.syntax.bad_subscript";

    /// <summary>
    /// The text has more problems than are listed: the first 100 found (<see cref="ConditionParser.MaxProblems"/>) are returned, in source order, and this one stands in place of the rest; its span is the text after the last problem
    /// listed. "Found" is not the order of the text: the lexical problems are found before the syntax ones, and an unclosed bracket after the problems inside it, so with enough problems an earlier one may not be listed.
    /// The recovery also stops when it has parsed the tokens as often as it is allowed (<see cref="Recovery.MaxAttempts"/>), with only a problem or two listed. Fix the listed ones and parse again to see the rest.
    /// </summary>
    public const string TooManyProblems = "strategy.syntax.too_many_problems";

    /// <summary><c>tf</c> not written as <c>tf("1h").name</c> or <c>tf("1h").name(args)</c>; the span depends on what is missing (see the message).</summary>
    public const string BadTimeframeQualifier = "strategy.syntax.bad_timeframe_qualifier";
}

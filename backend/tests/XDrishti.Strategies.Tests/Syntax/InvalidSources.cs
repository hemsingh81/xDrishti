using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>One problem a source must produce: the code, the exact span (offsets, line and column of the start) and, where the rule offers one, the suggestion.</summary>
public sealed record ExpectedProblem(string Code, int Start, int End, int Line, int Column, string? Suggestion = null);

/// <summary>
/// The invalid sources with the problems each must produce, in source order. Offsets are zero-based UTF-16 positions, the end is exclusive, lines and columns count from 1. The spans are worked out by hand from the
/// rules in <see cref="SyntaxCodes"/> (the offending token; the empty position after the last token for a text that stops too soon; the opening bracket for an unclosed one).
/// </summary>
public static class InvalidSources
{
    private static ExpectedProblem P(string code, int start, int end, int line, int column, string? suggestion = null) => new(code, start, end, line, column, suggestion);

    public static readonly IReadOnlyDictionary<string, ExpectedProblem[]> Cases = new Dictionary<string, ExpectedProblem[]>
    {
        // nothing to read
        [string.Empty] = [P(SyntaxCodes.Empty, 0, 0, 1, 1)],
        ["   "] = [P(SyntaxCodes.Empty, 0, 3, 1, 1)],
        ["\n\t "] = [P(SyntaxCodes.Empty, 0, 3, 1, 1)],

        // brackets
        ["("] = [P(SyntaxCodes.UnclosedBracket, 0, 1, 1, 1, ")")],
        ["(a"] = [P(SyntaxCodes.UnclosedBracket, 0, 1, 1, 1, ")")],
        ["((a)"] = [P(SyntaxCodes.UnclosedBracket, 0, 1, 1, 1, ")")],
        ["rsi(14"] = [P(SyntaxCodes.UnclosedBracket, 3, 4, 1, 4, ")")],
        ["rsi("] = [P(SyntaxCodes.UnclosedBracket, 3, 4, 1, 4, ")")],
        ["x[1"] = [P(SyntaxCodes.UnclosedBracket, 1, 2, 1, 2, "]")],
        ["rsi(14))"] = [P(SyntaxCodes.UnmatchedClosingBracket, 7, 8, 1, 8)],
        ["a)"] = [P(SyntaxCodes.UnmatchedClosingBracket, 1, 2, 1, 2)],
        ["]"] = [P(SyntaxCodes.UnmatchedClosingBracket, 0, 1, 1, 1)],
        ["(a]"] = [P(SyntaxCodes.UnmatchedClosingBracket, 2, 3, 1, 3, ")")],
        ["()"] = [P(SyntaxCodes.MissingOperand, 1, 2, 1, 2)],
        ["(a +)"] = [P(SyntaxCodes.MissingOperand, 4, 5, 1, 5)],

        // characters
        ["$"] = [P(SyntaxCodes.UnknownCharacter, 0, 1, 1, 1)],
        ["a $ b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3), P(SyntaxCodes.MissingOperator, 4, 5, 1, 5)],
        ["$ > 3"] = [P(SyntaxCodes.UnknownCharacter, 0, 1, 1, 1)],
        ["f($)"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3)],
        ["a $$ b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3), P(SyntaxCodes.UnknownCharacter, 3, 4, 1, 4), P(SyntaxCodes.MissingOperator, 5, 6, 1, 6)],
        ["a ＞ b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3, ">")],
        ["a\u00A0> b"] = [P(SyntaxCodes.UnknownCharacter, 1, 2, 1, 2, " ")],
        ["x\u200B"] = [P(SyntaxCodes.UnknownCharacter, 1, 2, 1, 2)],
        ["a ≥ b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3, ">=")],
        ["−5"] = [P(SyntaxCodes.UnknownCharacter, 0, 1, 1, 1, "-")],
        ["😀"] = [P(SyntaxCodes.UnknownCharacter, 0, 2, 1, 1)],
        ["é > 1"] = [P(SyntaxCodes.UnknownCharacter, 0, 1, 1, 1)],
        ["a & b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3, "and")],
        ["a && b"] = [P(SyntaxCodes.UnknownCharacter, 2, 4, 1, 3, "and")],
        ["a || b"] = [P(SyntaxCodes.UnknownCharacter, 2, 4, 1, 3, "or")],
        ["!a"] = [P(SyntaxCodes.UnknownCharacter, 0, 1, 1, 1, "not")],
        ["'abc'"] = [P(SyntaxCodes.UnknownCharacter, 0, 5, 1, 1, "\"abc\"")],
        ["“09:45”"] = [P(SyntaxCodes.UnknownCharacter, 0, 7, 1, 1, "\"09:45\"")],
        ["regime() == 'UP_LOW_VOL'"] = [P(SyntaxCodes.UnknownCharacter, 12, 24, 1, 13, "\"UP_LOW_VOL\"")],
        ["a +\n\n  $"] = [P(SyntaxCodes.UnknownCharacter, 7, 8, 3, 3)],

        // numbers and strings
        ["55abc"] = [P(SyntaxCodes.NumberFollowedByIdentifier, 0, 5, 1, 1)],
        ["5m"] = [P(SyntaxCodes.NumberFollowedByIdentifier, 0, 2, 1, 1)],
        ["1E5"] = [P(SyntaxCodes.NumberFollowedByIdentifier, 0, 3, 1, 1)],
        ["0x10"] = [P(SyntaxCodes.NumberFollowedByIdentifier, 0, 4, 1, 1)],
        ["1_000"] = [P(SyntaxCodes.NumberFollowedByIdentifier, 0, 5, 1, 1)],
        ["1e400"] = [P(SyntaxCodes.NumberOutOfRange, 0, 5, 1, 1)],
        ["1e-400"] = [P(SyntaxCodes.NumberOutOfRange, 0, 6, 1, 1)],
        ["x[99999999999]"] = [P(SyntaxCodes.NumberOutOfRange, 2, 13, 1, 3)],
        ["\"abc"] = [P(SyntaxCodes.UnterminatedString, 0, 4, 1, 1, "\"abc\"")],
        ["a > \""] = [P(SyntaxCodes.UnterminatedString, 4, 5, 1, 5, "\"\"")],

        // a string ends at a line break of either kind: a bare carriage return is a line break inside a string like a line feed, so the string is not closed, and the closing quote opens another
        ["\"09:\r45\""] = [P(SyntaxCodes.UnterminatedString, 0, 4, 1, 1, "\"09:\""), P(SyntaxCodes.UnterminatedString, 7, 8, 1, 8, "\"\"")],
        ["\"09:\n45\""] = [P(SyntaxCodes.UnterminatedString, 0, 4, 1, 1, "\"09:\""), P(SyntaxCodes.UnterminatedString, 7, 8, 2, 3, "\"\"")],
        ["time >= \"09:45\r\n\" and a"] = [P(SyntaxCodes.UnterminatedString, 8, 14, 1, 9, "\"09:45\""), P(SyntaxCodes.UnterminatedString, 16, 23, 2, 1, "\" and a\"")],

        // operators and operands
        ["a < b < c"] = [P(SyntaxCodes.ChainedComparison, 6, 7, 1, 7)],
        ["a == b != c"] = [P(SyntaxCodes.ChainedComparison, 7, 9, 1, 8)],
        ["a < b and c < d < e"] = [P(SyntaxCodes.ChainedComparison, 16, 17, 1, 17)],
        ["not a < b < c"] = [P(SyntaxCodes.ChainedComparison, 10, 11, 1, 11)],
        ["1 < 2 < 3 and $"] = [P(SyntaxCodes.ChainedComparison, 6, 7, 1, 7), P(SyntaxCodes.UnknownCharacter, 14, 15, 1, 15)],
        ["a b"] = [P(SyntaxCodes.MissingOperator, 2, 3, 1, 3)],
        ["rsi(14) 55"] = [P(SyntaxCodes.MissingOperator, 8, 10, 1, 9)],
        ["close \"09:45\""] = [P(SyntaxCodes.MissingOperator, 6, 13, 1, 7)],
        ["f(a b)"] = [P(SyntaxCodes.MissingOperator, 4, 5, 1, 5)],
        ["a\nb"] = [P(SyntaxCodes.MissingOperator, 2, 3, 2, 1)],
        ["a +"] = [P(SyntaxCodes.UnexpectedEnd, 3, 3, 1, 4)],
        ["a and"] = [P(SyntaxCodes.UnexpectedEnd, 5, 5, 1, 6)],
        ["not"] = [P(SyntaxCodes.UnexpectedEnd, 3, 3, 1, 4)],
        ["-"] = [P(SyntaxCodes.UnexpectedEnd, 1, 1, 1, 2)],
        ["a < b < c and d <"] = [P(SyntaxCodes.ChainedComparison, 6, 7, 1, 7), P(SyntaxCodes.UnexpectedEnd, 17, 17, 1, 18)],
        ["rsi(14 >"] = [P(SyntaxCodes.UnclosedBracket, 3, 4, 1, 4, ")"), P(SyntaxCodes.UnexpectedEnd, 8, 8, 1, 9)],
        ["a + * b"] = [P(SyntaxCodes.MissingOperand, 4, 5, 1, 5)],
        ["> 55"] = [P(SyntaxCodes.MissingOperand, 0, 1, 1, 1)],
        ["a or or b"] = [P(SyntaxCodes.MissingOperand, 5, 7, 1, 6)],
        ["["] = [P(SyntaxCodes.MissingOperand, 0, 1, 1, 1)],
        ["."] = [P(SyntaxCodes.MissingOperand, 0, 1, 1, 1)],
        ["a < not b"] = [P(SyntaxCodes.UnexpectedToken, 4, 7, 1, 5)],
        [","] = [P(SyntaxCodes.UnexpectedToken, 0, 1, 1, 1)],
        ["(a, b)"] = [P(SyntaxCodes.UnexpectedToken, 2, 3, 1, 3)],
        ["a = b"] = [P(SyntaxCodes.UnexpectedToken, 2, 3, 1, 3, "==")],

        // arguments
        ["rsi(14,)"] = [P(SyntaxCodes.TrailingComma, 6, 7, 1, 7)],
        ["rsi(,14)"] = [P(SyntaxCodes.MissingArgument, 4, 5, 1, 5)],
        ["rsi(14,,15)"] = [P(SyntaxCodes.MissingArgument, 7, 8, 1, 8)],
        ["f(a,\n  b,\n)"] = [P(SyntaxCodes.TrailingComma, 8, 9, 2, 4)],
        ["f(source=)"] = [P(SyntaxCodes.MissingOperand, 9, 10, 1, 10)],
        ["f(source=1)"] = [P(SyntaxCodes.UnexpectedToken, 9, 10, 1, 10)],
        ["f(1=x)"] = [P(SyntaxCodes.UnexpectedToken, 3, 4, 1, 4, "==")],

        // one mistake, one problem: a follow-on problem is not reported on a token that has one, nor on a value the recovery supplied, nor at an end it made itself
        ["close <> 5"] = [P(SyntaxCodes.MissingOperand, 7, 8, 1, 8)],
        ["close > > 5"] = [P(SyntaxCodes.MissingOperand, 8, 9, 1, 9)],
        ["close < > 5"] = [P(SyntaxCodes.MissingOperand, 8, 9, 1, 9)],
        ["rsi(,)"] = [P(SyntaxCodes.MissingArgument, 4, 5, 1, 5)],
        ["rsi(,,)"] = [P(SyntaxCodes.MissingArgument, 4, 5, 1, 5), P(SyntaxCodes.MissingArgument, 5, 6, 1, 6)],
        ["f(a,,)"] = [P(SyntaxCodes.MissingArgument, 4, 5, 1, 5)],
        ["close ! 5"] = [P(SyntaxCodes.UnknownCharacter, 6, 7, 1, 7, "not")],
        ["a !! b"] = [P(SyntaxCodes.UnknownCharacter, 2, 3, 1, 3, "not"), P(SyntaxCodes.UnknownCharacter, 3, 4, 1, 4, "not")],
        ["close === 5"] = [P(SyntaxCodes.UnexpectedToken, 8, 9, 1, 9, "==")],
        ["close >= = 5"] = [P(SyntaxCodes.UnexpectedToken, 9, 10, 1, 10, "==")],
        ["close =="] = [P(SyntaxCodes.UnexpectedEnd, 8, 8, 1, 9)],

        // subscripts
        ["highest(20)[1][2]"] = [P(SyntaxCodes.SubscriptOnNonReference, 14, 15, 1, 15)],
        ["highest(20)["] = [P(SyntaxCodes.UnclosedBracket, 11, 12, 1, 12, "]")],
        ["highest(20)[1"] = [P(SyntaxCodes.UnclosedBracket, 11, 12, 1, 12, "]")],
        ["(a)[1]"] = [P(SyntaxCodes.SubscriptOnNonReference, 3, 4, 1, 4)],
        ["x[1][2]"] = [P(SyntaxCodes.SubscriptOnNonReference, 4, 5, 1, 5)],
        ["1[2]"] = [P(SyntaxCodes.SubscriptOnNonReference, 1, 2, 1, 2)],
        ["(close + open)[1]"] = [P(SyntaxCodes.SubscriptOnNonReference, 14, 15, 1, 15)],
        ["x[]"] = [P(SyntaxCodes.BadSubscript, 2, 3, 1, 3)],
        ["x[a]"] = [P(SyntaxCodes.BadSubscript, 2, 3, 1, 3)],
        ["x[1.5]"] = [P(SyntaxCodes.BadSubscript, 2, 5, 1, 3)],
        ["x[1 2]"] = [P(SyntaxCodes.BadSubscript, 4, 5, 1, 5)],

        // names and the timeframe qualifier
        ["a."] = [P(SyntaxCodes.UnexpectedEnd, 2, 2, 1, 3)],
        ["a.1"] = [P(SyntaxCodes.UnexpectedToken, 2, 3, 1, 3)],
        ["a.b.c"] = [P(SyntaxCodes.UnexpectedToken, 3, 4, 1, 4)],
        ["tf(\"1h\")"] = [P(SyntaxCodes.BadTimeframeQualifier, 0, 8, 1, 1)],
        ["tf(\"1h\") > 3"] = [P(SyntaxCodes.BadTimeframeQualifier, 0, 8, 1, 1)],
        ["tf(\"1h\")."] = [P(SyntaxCodes.BadTimeframeQualifier, 9, 9, 1, 10)],
        ["tf(15).ema(20)"] = [P(SyntaxCodes.BadTimeframeQualifier, 3, 5, 1, 4)],
        ["tf(\"1h\".close"] = [P(SyntaxCodes.BadTimeframeQualifier, 7, 8, 1, 8)],
    };

    /// <summary>The sources, as theory data: one test case per source.</summary>
    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        foreach (var source in Cases.Keys)
        {
            data.Add(source);
        }

        return data;
    }
}

using System.Globalization;
using Superpower.Model;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The first stage of a parse: runs the lexer and turns what is wrong at the character level into problems, all of them, in source order. The tokens that come out are the ones the grammar sees: a wrong token is
/// replaced by a <see cref="Tk.Missing"/> value where a value is due, and dropped where it would only be in the way (after a value, or a space look-alike), so one mistake does not become a chain of errors.
/// </summary>
internal static class LexicalPass
{
    public static (List<Token<Tk>> Tokens, List<Problem> Problems) Run(string source)
    {
        var tokens = new List<Token<Tk>>();
        var problems = new List<Problem>();
        var result = Lexer.Instance.TryTokenize(source);
        if (!result.HasValue)
        {
            // The lexer reads any character, so this is not expected; report it rather than assume.
            var at = result.ErrorPosition.HasValue ? result.ErrorPosition.Absolute : 0;
            problems.Add(new Problem(SyntaxCodes.UnexpectedToken, "The text could not be read: " + result.ErrorMessage, SourceText.SpanOf(source, at, Math.Min(at + 1, source.Length))));
            return (tokens, problems);
        }

        foreach (var token in result.Value)
        {
            switch (token.Kind)
            {
                case Tk.Unknown:
                    ReadUnknown(token, tokens, problems);
                    break;
                case Tk.UnterminatedString:
                    problems.Add(new Problem(
                        SyntaxCodes.UnterminatedString,
                        "The string " + Shown(token) + " has no closing double quote on its line.",
                        Grammar.SpanOf(token),
                        Suggestion: Shown(token) + "\""));
                    AddUnreadable(token, tokens);
                    break;
                case Tk.BadNumber:
                    problems.Add(new Problem(
                        SyntaxCodes.NumberFollowedByIdentifier,
                        "A number cannot be followed directly by letters, digits or underscores: " + Shown(token) + ". Separate them, or write the exponent as 1e-3.",
                        Grammar.SpanOf(token)));
                    AddUnreadable(token, tokens);
                    break;
                case Tk.Number when OutOfRange(token, tokens) is { } message:
                    problems.Add(new Problem(SyntaxCodes.NumberOutOfRange, message, Grammar.SpanOf(token)));
                    AddUnreadable(token, tokens);
                    break;
                default:
                    tokens.Add(token);
                    break;
            }
        }

        return (tokens, problems);
    }

    private static void ReadUnknown(Token<Tk> token, List<Token<Tk>> tokens, List<Problem> problems)
    {
        var text = token.ToStringValue();
        var reading = UnknownCharacters.Read(text);
        problems.Add(new Problem(SyntaxCodes.UnknownCharacter, UnknownCharacters.Message(text), Grammar.SpanOf(token), reading.Suggestion));
        if (reading.Stands != Tk.None)
        {
            tokens.Add(new Token<Tk>(reading.Stands, token.Span));
        }
        else if (!reading.Drop)
        {
            AddUnreadable(token, tokens);
        }
    }

    /// <summary>A token that was reported and cannot be used: a missing value in its place if a value can start here, nothing if the previous token already ended a value.</summary>
    private static void AddUnreadable(Token<Tk> token, List<Token<Tk>> tokens)
    {
        if (tokens.Count == 0 || !TokenRules.EndsOperand(tokens[^1].Kind))
        {
            tokens.Add(new Token<Tk>(Tk.Missing, token.Span));
        }
    }

    /// <summary>The reason a number token cannot be used, or null: too large or too small for a double, or a subscript that does not fit an integer.</summary>
    private static string? OutOfRange(Token<Tk> token, List<Token<Tk>> before)
    {
        var text = token.ToStringValue();
        var inSubscript = before.Count > 0 && (before[^1].Kind == Tk.LeftBracket || (before[^1].Kind == Tk.Minus && before.Count > 1 && before[^2].Kind == Tk.LeftBracket));
        if (inSubscript && text.All(char.IsAsciiDigit) && !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return "The subscript " + text + " is too large; a subscript is a small whole number of bars.";
        }

        var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value))
        {
            return "The number " + Shorten(text) + " is too large to be represented.";
        }

        if (value == 0 && HasNonZeroMantissa(text))
        {
            return "The number " + Shorten(text) + " is too small to be represented.";
        }

        return null;
    }

    private static bool HasNonZeroMantissa(string text)
    {
        foreach (var c in text)
        {
            if (c == 'e')
            {
                return false;
            }

            if (c is >= '1' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    private static string Shown(Token<Tk> token) => Shorten(token.ToStringValue());

    internal static string Shorten(string text) => text.Length <= 30 ? text : string.Concat(text.AsSpan(0, 29), "…");
}

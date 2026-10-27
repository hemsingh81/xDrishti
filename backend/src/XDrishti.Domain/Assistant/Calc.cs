using System.Globalization;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// The arithmetic of the <c>calc</c> tool (spec P9-05 AC 11): a small recursive-descent parser over <see cref="decimal"/> for numbers, <c>+ - * / ( )</c>, unary minus and the functions <c>round abs min max</c>. The text is never handed to the runtime to evaluate: an identifier, <c>^</c>, <c>**</c>, a string,
/// an exponent, more than 50 tokens or 200 characters is rejected. The result is written as decimal text, so a figure is never a binary float that gets rounded twice. Pure.
/// </summary>
public static class Calc
{
    public const int MaxChars = 200;
    public const int MaxTokens = 50;
    public const int DefaultPlaces = 4;
    public const int MaxPlaces = 8;

    private const string Rejected = "calc.rejected";
    private const string DivideByZero = "calc.divide_by_zero";

    public static CalcResult Evaluate(string expression, int places = DefaultPlaces)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Length is 0 or > MaxChars)
        {
            return Reject("chars");
        }

        if (places is < 0 or > MaxPlaces)
        {
            return Reject("places");
        }

        var tokens = Tokenise(expression, out var problem);
        if (tokens is null)
        {
            return Reject(problem!);
        }

        if (tokens.Count == 0 || tokens.Count > MaxTokens)
        {
            return Reject("tokens");
        }

        try
        {
            var parser = new Parser(tokens);
            var value = parser.ParseAll();
            if (value is null)
            {
                return Reject(parser.Problem ?? "syntax");
            }

            return new CalcResult(Math.Round(value.Value, places, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture), null, null);
        }
        catch (DivideByZeroException)
        {
            return new CalcResult(null, DivideByZero, "divide_by_zero");
        }
        catch (OverflowException)
        {
            return Reject("overflow");
        }
    }

    private static CalcResult Reject(string reason) => new(null, Rejected, reason);

    private static List<Token>? Tokenise(string text, out string? problem)
    {
        problem = null;
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is ' ' or '\t')
            {
                i++;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }
                }

                if (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_' || text[i] == '.'))
                {
                    problem = "number";
                    return null;
                }

                if (!decimal.TryParse(text.AsSpan(start, i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    problem = "number";
                    return null;
                }

                tokens.Add(new Token(TokenKind.Number, number, null));
            }
            else if (c is '+' or '-' or '/' or '(' or ')' or ',')
            {
                tokens.Add(new Token(TokenKind.Symbol, 0, c.ToString()));
                i++;
            }
            else if (c == '*')
            {
                if (i + 1 < text.Length && text[i + 1] == '*')
                {
                    problem = "operator";
                    return null;
                }

                tokens.Add(new Token(TokenKind.Symbol, 0, "*"));
                i++;
            }
            else if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                var name = text[start..i];
                if (name is not ("round" or "abs" or "min" or "max") || i >= text.Length || text.AsSpan(i).TrimStart(' ').IsEmpty || text.AsSpan(i).TrimStart(' ')[0] != '(')
                {
                    problem = "identifier";
                    return null;
                }

                tokens.Add(new Token(TokenKind.Function, 0, name));
            }
            else
            {
                problem = c is '^' ? "operator" : "character";
                return null;
            }

            if (tokens.Count > MaxTokens)
            {
                problem = "tokens";
                return null;
            }
        }

        return tokens;
    }

    private enum TokenKind
    {
        Number,
        Symbol,
        Function,
    }

    private sealed record Token(TokenKind Kind, decimal Number, string? Text);

    private sealed class Parser(List<Token> tokens)
    {
        private const int MaxDepth = 20;
        private int _position;
        private int _depth;

        public string? Problem { get; private set; }

        public decimal? ParseAll()
        {
            var value = Expression();
            if (value is null)
            {
                return null;
            }

            if (_position != tokens.Count)
            {
                Problem = "syntax";
                return null;
            }

            return value;
        }

        private bool Accept(string symbol)
        {
            if (_position < tokens.Count && tokens[_position] is { Kind: TokenKind.Symbol } t && t.Text == symbol)
            {
                _position++;
                return true;
            }

            return false;
        }

        private decimal? Expression()
        {
            var left = Term();
            while (left is not null)
            {
                if (Accept("+"))
                {
                    var right = Term();
                    left = right is null ? null : left + right;
                }
                else if (Accept("-"))
                {
                    var right = Term();
                    left = right is null ? null : left - right;
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private decimal? Term()
        {
            var left = Unary();
            while (left is not null)
            {
                if (Accept("*"))
                {
                    var right = Unary();
                    left = right is null ? null : left * right;
                }
                else if (Accept("/"))
                {
                    var right = Unary();
                    if (right is null)
                    {
                        return null;
                    }

                    if (right == 0m)
                    {
                        throw new DivideByZeroException();
                    }

                    left /= right;
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private decimal? Unary()
        {
            if (Accept("-"))
            {
                var value = Unary();
                return value is null ? null : -value;
            }

            if (Accept("+"))
            {
                return Unary();
            }

            return Primary();
        }

        private decimal? Primary()
        {
            if (_position >= tokens.Count)
            {
                Problem = "syntax";
                return null;
            }

            var token = tokens[_position];
            if (token.Kind == TokenKind.Number)
            {
                _position++;
                return token.Number;
            }

            if (token.Kind == TokenKind.Function)
            {
                _position++;
                return Call(token.Text!);
            }

            if (Accept("("))
            {
                if (++_depth > MaxDepth)
                {
                    Problem = "depth";
                    return null;
                }

                var inner = Expression();
                _depth--;
                if (inner is null || !Accept(")"))
                {
                    Problem ??= "syntax";
                    return null;
                }

                return inner;
            }

            Problem = "syntax";
            return null;
        }

        private decimal? Call(string name)
        {
            if (!Accept("("))
            {
                Problem = "syntax";
                return null;
            }

            if (++_depth > MaxDepth)
            {
                Problem = "depth";
                return null;
            }

            var arguments = new List<decimal>();
            do
            {
                var value = Expression();
                if (value is null)
                {
                    return null;
                }

                arguments.Add(value.Value);
            }
            while (Accept(","));
            _depth--;
            if (!Accept(")"))
            {
                Problem ??= "syntax";
                return null;
            }

            switch (name)
            {
                case "abs" when arguments.Count == 1:
                    return Math.Abs(arguments[0]);
                case "min" when arguments.Count >= 2:
                    return arguments.Min();
                case "max" when arguments.Count >= 2:
                    return arguments.Max();
                case "round" when arguments.Count is 1 or 2:
                    var digits = arguments.Count == 2 ? arguments[1] : 0m;
                    if (digits != decimal.Truncate(digits) || digits is < 0 or > 10)
                    {
                        Problem = "places";
                        return null;
                    }

                    return Math.Round(arguments[0], (int)digits, MidpointRounding.AwayFromZero);
                default:
                    Problem = "arguments";
                    return null;
            }
        }
    }
}

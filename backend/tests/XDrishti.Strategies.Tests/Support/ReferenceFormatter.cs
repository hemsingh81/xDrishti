using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// A second, deliberately different way to write a tree as text, to show that the parser does not depend on how the text was laid out: every operator application is wrapped in parentheses and the tokens are
/// separated by random white space (spaces, tabs, line feeds, carriage returns; none where the tokens are punctuation and the white space would be optional is also tried).
/// </summary>
public static partial class ReferenceFormatter
{
    public static List<string> Tokens(Expression expression)
    {
        var tokens = new List<string>();
        Write(tokens, expression);
        return tokens;
    }

    public static string Laid(Expression expression, Random random)
    {
        var tokens = Tokens(expression);
        var text = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                var needsGap = IsWordEdge(tokens[i - 1][^1]) && IsWordEdge(tokens[i][0]);
                text.Append(Gap(random, needsGap));
            }

            text.Append(tokens[i]);
        }

        return Gap(random, false) + text + Gap(random, false);
    }

    private static string Gap(Random random, bool required)
    {
        var length = required ? random.Next(1, 4) : random.Next(0, 3);
        var gap = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            gap.Append(random.Next(6) switch { 0 => '\t', 1 => '\n', 2 => '\r', _ => ' ' });
        }

        return gap.ToString();
    }

    private static bool IsWordEdge(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '"';

    private static void Write(List<string> tokens, Expression expression)
    {
        switch (expression)
        {
            case NumberLiteral number:
                tokens.Add(Digits(number.Value));
                break;
            case StringLiteral text:
                tokens.Add("\"" + text.Value + "\"");
                break;
            case BoolLiteral boolean:
                tokens.Add(boolean.Value ? "true" : "false");
                break;
            case Reference reference:
                Qualifier(tokens, reference.Scope, reference.Timeframe);
                tokens.Add(reference.Name.Text);
                break;
            case Call call:
                Qualifier(tokens, null, call.Timeframe);
                tokens.Add(call.Name.Text);
                tokens.Add("(");
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (i > 0)
                    {
                        tokens.Add(",");
                    }

                    switch (call.Arguments[i])
                    {
                        case PositionalArgument positional:
                            Write(tokens, positional.Value);
                            break;
                        case NamedArgument named:
                            tokens.AddRange([named.Name.Text, "=", named.Value.Text]);
                            break;
                    }
                }

                tokens.Add(")");
                break;
            case Subscript subscript:
                Write(tokens, subscript.Target);
                tokens.Add("[");
                if (subscript.Lag < 0)
                {
                    tokens.Add("-");
                }

                tokens.Add(Math.Abs(subscript.Lag).ToString(CultureInfo.InvariantCulture));
                tokens.Add("]");
                break;
            case Unary unary:
                tokens.Add("(");
                tokens.Add(unary.Operator == UnaryOperator.Not ? "not" : "-");
                Write(tokens, unary.Operand);
                tokens.Add(")");
                break;
            case Binary binary:
                tokens.Add("(");
                Write(tokens, binary.Left);
                tokens.Add(binary.Operator switch
                {
                    BinaryOperator.Or => "or",
                    BinaryOperator.And => "and",
                    BinaryOperator.Less => "<",
                    BinaryOperator.LessOrEqual => "<=",
                    BinaryOperator.Greater => ">",
                    BinaryOperator.GreaterOrEqual => ">=",
                    BinaryOperator.Equal => "==",
                    BinaryOperator.NotEqual => "!=",
                    BinaryOperator.Add => "+",
                    BinaryOperator.Subtract => "-",
                    BinaryOperator.Multiply => "*",
                    _ => "/",
                });
                Write(tokens, binary.Right);
                tokens.Add(")");
                break;
        }
    }

    private static void Qualifier(List<string> tokens, Identifier? scope, StringLiteral? timeframe)
    {
        if (scope is not null)
        {
            tokens.AddRange([scope.Text, "."]);
        }
        else if (timeframe is not null)
        {
            tokens.AddRange(["tf", "(", "\"" + timeframe.Value + "\"", ")", "."]);
        }
    }

    /// <summary>A number as the lexer reads it: plain digits or a mantissa with a lower-case <c>e</c>.</summary>
    private static string Digits(double value) => ExponentSign().Replace(value.ToString("R", CultureInfo.InvariantCulture), m => "e" + (m.Groups[1].Value == "-" ? "-" : string.Empty) + m.Groups[2].Value);

    [GeneratedRegex("E([+-])0*([0-9]+)")]
    private static partial Regex ExponentSign();
}

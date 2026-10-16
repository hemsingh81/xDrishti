using System.Globalization;
using System.Text;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The canonical text of a syntax tree: one space around infix operators, <c>, </c> between arguments, <c>not </c> before its operand, the fewest parentheses that keep the meaning, numbers in their shortest
/// round-trip form (<c>0.001</c>, <c>1e-7</c>), no comments, one line. Two sources that differ only in white space or in redundant parentheses print the same, and <c>Parse(Print(tree))</c> equals <c>tree</c>
/// (equality ignores positions). A tree that cannot be written as text (a string with a double quote or a line break, a node with both a scope and a timeframe) is a programming error and throws;
/// a tree the parser returned never does. Recursion follows the depth of the tree, which the parser limits.
/// </summary>
public static class ExpressionPrinter
{
    private const int LevelOr = 1;
    private const int LevelAnd = 2;
    private const int LevelNot = 3;
    private const int LevelCompare = 4;
    private const int LevelSum = 5;
    private const int LevelProduct = 6;
    private const int LevelUnary = 7;
    private const int LevelAtom = 8;

    /// <summary>The canonical text of the expression.</summary>
    public static string Print(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var text = new StringBuilder();
        Write(text, expression);
        return text.ToString();
    }

    private static void Write(StringBuilder text, Expression expression)
    {
        switch (expression)
        {
            case NumberLiteral number:
                text.Append(Number(number.Value));
                break;
            case StringLiteral literal:
                WriteString(text, literal.Value);
                break;
            case BoolLiteral boolean:
                text.Append(boolean.Value ? "true" : "false");
                break;
            case Reference reference:
                WriteQualifier(text, reference.Scope, reference.Timeframe);
                text.Append(reference.Name.Text);
                break;
            case Call call:
                WriteQualifier(text, null, call.Timeframe);
                text.Append(call.Name.Text).Append('(');
                WriteArguments(text, call);
                text.Append(')');
                break;
            case Subscript subscript:
                Write(text, subscript.Target);
                text.Append('[').Append(subscript.Lag.ToString(CultureInfo.InvariantCulture)).Append(']');
                break;
            case Unary unary:
                WriteUnary(text, unary);
                break;
            case Binary binary:
                WriteBinary(text, binary);
                break;
            default:
                throw new ArgumentException("Unknown expression node " + expression.GetType().Name + ".", nameof(expression));
        }
    }

    private static void WriteQualifier(StringBuilder text, Identifier? scope, StringLiteral? timeframe)
    {
        if (scope is not null && timeframe is not null)
        {
            throw new ArgumentException("A reference has either a scope or a timeframe, not both.");
        }

        if (scope is not null)
        {
            text.Append(scope.Text).Append('.');
        }
        else if (timeframe is not null)
        {
            text.Append("tf(");
            WriteString(text, timeframe.Value);
            text.Append(").");
        }
    }

    private static void WriteArguments(StringBuilder text, Call call)
    {
        if (call.Arguments.IsDefaultOrEmpty)
        {
            return;
        }

        for (var i = 0; i < call.Arguments.Length; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            switch (call.Arguments[i])
            {
                case PositionalArgument positional:
                    Write(text, positional.Value);
                    break;
                case NamedArgument named:
                    text.Append(named.Name.Text).Append('=').Append(named.Value.Text);
                    break;
                default:
                    throw new ArgumentException("Unknown argument node " + call.Arguments[i].GetType().Name + ".");
            }
        }
    }

    private static void WriteUnary(StringBuilder text, Unary unary)
    {
        if (unary.Operator == UnaryOperator.Not)
        {
            text.Append("not ");
            WriteChild(text, unary.Operand, Level(unary) > Level(unary.Operand));
            return;
        }

        text.Append('-');
        WriteChild(text, unary.Operand, Level(unary.Operand) < LevelAtom);
    }

    private static void WriteBinary(StringBuilder text, Binary binary)
    {
        var level = Level(binary);
        var comparison = level == LevelCompare;
        WriteChild(text, binary.Left, Level(binary.Left) < level || (comparison && Level(binary.Left) == level));
        text.Append(' ').Append(Symbol(binary.Operator)).Append(' ');
        WriteChild(text, binary.Right, Level(binary.Right) <= level);
    }

    private static void WriteChild(StringBuilder text, Expression child, bool parenthesise)
    {
        if (parenthesise)
        {
            text.Append('(');
        }

        Write(text, child);
        if (parenthesise)
        {
            text.Append(')');
        }
    }

    private static int Level(Expression expression) => expression switch
    {
        Binary binary => binary.Operator switch
        {
            BinaryOperator.Or => LevelOr,
            BinaryOperator.And => LevelAnd,
            BinaryOperator.Add or BinaryOperator.Subtract => LevelSum,
            BinaryOperator.Multiply or BinaryOperator.Divide => LevelProduct,
            _ => LevelCompare,
        },
        Unary { Operator: UnaryOperator.Not } => LevelNot,
        Unary => LevelUnary,
        _ => LevelAtom,
    };

    private static string Symbol(BinaryOperator op) => op switch
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
        BinaryOperator.Divide => "/",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    private static void WriteString(StringBuilder text, string value)
    {
        if (value.AsSpan().IndexOfAny('"', '\r', '\n') >= 0)
        {
            throw new ArgumentException("A string with a double quote or a line break cannot be written: the language has no escapes.");
        }

        text.Append('"').Append(value).Append('"');
    }

    /// <summary>
    /// The shortest text that reads back as the same number, written like ECMAScript writes a number (and without a plus sign): the shortest digits that round-trip, in plain notation when the decimal point lies
    /// within 21 digits to the right or 5 zeros to the left of them (<c>1000000</c>, <c>0.00001</c>), otherwise a mantissa, <c>e</c> and the exponent (<c>1e21</c>, <c>1e-7</c>, <c>1.5e-9</c>).
    /// </summary>
    private static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("A number literal is finite.");
        }

        if (value == 0)
        {
            return "0";
        }

        // The shortest round-trip form of .NET, taken apart into its digits and the position of the decimal point: value = 0.DIGITS x 10^point.
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = text.IndexOf('E', StringComparison.Ordinal);
        if (e >= 0)
        {
            exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var point = (dot < 0 ? text.Length : dot) + exponent;
        var digits = text.Replace(".", string.Empty, StringComparison.Ordinal);
        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0').TrimEnd('0');
        point -= leading;

        var sign = value < 0 ? "-" : string.Empty;
        if (digits.Length <= point && point <= 21)
        {
            return sign + digits + new string('0', point - digits.Length);
        }

        if (0 < point && point <= 21)
        {
            return sign + digits[..point] + "." + digits[point..];
        }

        if (-6 < point && point <= 0)
        {
            return sign + "0." + new string('0', -point) + digits;
        }

        var mantissa = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
        return sign + mantissa + "e" + (point - 1).ToString(CultureInfo.InvariantCulture);
    }
}

using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>Comparisons: two numbers, two true/false values (equal and not equal), <c>time</c> against a <c>"HH:MM"</c> text, and <c>regime()</c> against a regime name or a code (equal and not equal).</summary>
internal sealed partial class Binder
{
    private const string ComparisonRule =
        " A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).";

    private IBoundExpression BindComparison(Binary binary)
    {
        var left = Bind(binary.Left);
        var right = Bind(binary.Right);
        if (left.Type == BoundType.Invalid || right.Type == BoundType.Invalid)
        {
            return new BoundInvalid(binary.Span, [left, right]);
        }

        if (left.Type == BoundType.Time || right.Type == BoundType.Time)
        {
            return BindTimeComparison(binary, left, right);
        }

        if (left.Type == BoundType.Regime || right.Type == BoundType.Regime)
        {
            return BindRegimeComparison(binary, left, right);
        }

        var equality = binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual;
        if ((left.Type == BoundType.Number && right.Type == BoundType.Number) || (left.Type == BoundType.Bool && right.Type == BoundType.Bool && equality))
        {
            return new BoundCompare(binary.Operator, left, right, binary.Span);
        }

        return Mismatch(binary, left, right);
    }

    private BoundInvalid Mismatch(Binary binary, IBoundExpression left, IBoundExpression right)
    {
        scope.Error(
            LintCodes.TypeMismatch,
            "`" + Describe.Symbol(binary.Operator) + "` cannot compare " + scope.Quoted(left.Span) + " (" + Describe.Type(left.Type) + ") with " + scope.Quoted(right.Span) + " (" + Describe.Type(right.Type) + ")." + ComparisonRule,
            binary.Span);
        return new BoundInvalid(binary.Span, [left, right]);
    }

    /// <summary><c>time</c> with a text on either side; the node always has <c>time</c> on the left.</summary>
    private IBoundExpression BindTimeComparison(Binary binary, IBoundExpression left, IBoundExpression right)
    {
        BoundTimeKeyword keyword;
        BoundText text;
        var op = binary.Operator;
        if (left is BoundTimeKeyword leftKeyword && right is BoundText rightText)
        {
            (keyword, text) = (leftKeyword, rightText);
        }
        else if (left is BoundText leftText && right is BoundTimeKeyword rightKeyword)
        {
            (keyword, text) = (rightKeyword, leftText);
            op = Describe.Mirror(op);
        }
        else
        {
            return Mismatch(binary, left, right);
        }

        // a text that is not a time of day keeps the keyword and the text as parts: `time` on a daily strategy is a fault of its own, to be reported with it
        return TryTimeOfDay(text, out var minute) ? new BoundTimeCompare(op, minute, keyword.Span, binary.Span) : new BoundInvalid(binary.Span, [keyword, text]);
    }

    /// <summary>The text as minutes after midnight, within the session; otherwise a <c>bad_time_literal</c> (with the corrected text where the fix is obvious).</summary>
    private bool TryTimeOfDay(BoundText text, out int minute)
    {
        minute = 0;
        var value = text.Value;
        if (!TryClock(value, out minute))
        {
            var fixedText = value.Trim().Replace('.', ':');
            if (fixedText.Length == 4 && fixedText[1] == ':')
            {
                fixedText = "0" + fixedText;
            }

            var suggestion = fixedText != value && TryClock(fixedText, out _) ? "\"" + fixedText + "\"" : null;
            scope.Error(
                LintCodes.BadTimeLiteral,
                scope.Quoted(text.Span) + " is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
                text.Span,
                suggestion);
            return false;
        }

        if (minute is < SessionClock.OpenMinute or > SessionClock.CloseMinute)
        {
            scope.Error(
                LintCodes.BadTimeLiteral,
                scope.Quoted(text.Span) + " is outside the session: `time` is the minute at which a bar closes, from 09:15 to 15:30.",
                text.Span);
            return false;
        }

        return true;
    }

    /// <summary>Whether the text is exactly two digits, a colon and two digits that make a time of day (00:00 to 23:59).</summary>
    private static bool TryClock(string text, out int minute)
    {
        minute = 0;
        if (text.Length != 5 || text[2] != ':' || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[3]) || !char.IsAsciiDigit(text[4]))
        {
            return false;
        }

        var hours = (text[0] - '0') * 10 + (text[1] - '0');
        var minutes = (text[3] - '0') * 10 + (text[4] - '0');
        if (hours > 23 || minutes > 59)
        {
            return false;
        }

        minute = hours * 60 + minutes;
        return true;
    }

    /// <summary><c>regime()</c> with a regime name or a constant code 1 to 9, equal or not equal only; the result is a comparison of the feature with the code.</summary>
    private IBoundExpression BindRegimeComparison(Binary binary, IBoundExpression left, IBoundExpression right)
    {
        var regimeOnLeft = left.Type == BoundType.Regime;
        var regime = regimeOnLeft ? left : right;
        var other = regimeOnLeft ? right : left;
        if (other.Type == BoundType.Regime || binary.Operator is not (BinaryOperator.Equal or BinaryOperator.NotEqual))
        {
            return Mismatch(binary, left, right);
        }

        int code;
        if (other is BoundText name)
        {
            var parsed = Regime.Code(name.Value);
            if (parsed.IsFailure)
            {
                ReportUnknownRegime(name);
                return new BoundInvalid(binary.Span, [left, right]);
            }

            code = parsed.Value;
        }
        else if (other.Type == BoundType.Number && ConstantFolder.TryFold(other, out var number))
        {
            if (number != Math.Floor(number) || number is < 1 or > 9)
            {
                scope.Error(
                    LintCodes.TypeMismatch,
                    scope.Quoted(other.Span) + " is not a regime code: `regime()` is compared with a regime name or a code from 1 (" + Regime.Name(1) + ") to 9 (" + Regime.Name(9) + ").",
                    binary.Span);
                return new BoundInvalid(binary.Span, [left, right]);
            }

            code = (int)number;
        }
        else
        {
            return Mismatch(binary, left, right);
        }

        return new BoundCompare(binary.Operator, regime, new BoundNumber(code, other.Span), binary.Span);
    }

    private void ReportUnknownRegime(BoundText name)
    {
        var names = Enumerable.Range(1, 9).Select(Regime.Name).ToList();
        var close = Suggestions.Closest(name.Value, names);
        scope.Error(
            LintCodes.UnknownName,
            scope.Quoted(name.Span) + " is not a market regime; the names are " + string.Join(", ", names.Take(8)) + " and " + names[8] + "."
                + (close.Count > 0 ? " Did you mean `\"" + close[0] + "\"`?" : string.Empty),
            name.Span,
            close.Count > 0 ? "\"" + close[0] + "\"" : null);
    }
}

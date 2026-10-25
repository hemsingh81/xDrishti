using System.Globalization;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// <c>never_true</c> (warning, spec P3-02f Part A): a comparison that cannot be true by construction, which makes a rule that silently never fires. Two shapes: a catalog feature against a number outside the values
/// the feature can take (<c>rsi(14) &gt; 100</c>, see <see cref="ValueRange"/>; the number can be a strategy parameter), and a raw bar field against a <c>highest</c>/<c>lowest</c> window that contains the current bar
/// (<c>close &gt; highest(20)</c>: the high of the window is at least the close; <c>crosses_above</c> is the same comparison on the current bar). The comparison is reported once, whole, with the spelling that works as
/// the suggestion: the window read one bar back (<c>close &gt; highest(20)[1]</c>), or the same comparison at the limit of the range.
/// </summary>
internal static class NeverTrueRule
{
    public static void Apply(IBoundExpression node, BindingScope scope)
    {
        switch (node)
        {
            case BoundCompare compare:
                Check(compare.Operator, compare.Left, compare.Right, compare.Span, scope, crossing: false);
                break;
            case BoundCrossing crossing:
                Check(crossing.IsAbove ? BinaryOperator.Greater : BinaryOperator.Less, crossing.Left, crossing.Right, crossing.Span, scope, crossing: true);
                break;
        }

        foreach (var child in BoundTree.Children(node))
        {
            Apply(child, scope);
        }
    }

    private static void Check(BinaryOperator op, IBoundExpression left, IBoundExpression right, SourceSpan span, BindingScope scope, bool crossing)
    {
        switch (left, right)
        {
            case (BoundFeature feature, not BoundFeature) when ConstantFolder.TryFold(right, out var value) && double.IsFinite(value):
                CheckRange(op, feature, value, scope.Text(feature.Span), span, scope, featureFirst: true, crossing);
                break;
            case (not BoundFeature, BoundFeature feature) when ConstantFolder.TryFold(left, out var value) && double.IsFinite(value):
                CheckRange(Flip(op), feature, value, scope.Text(feature.Span), span, scope, featureFirst: false, crossing);
                break;
            case (BoundFeature { Lag: 0 } a, BoundFeature { Lag: 0 } b) when IsImpossibleWindow(op, a, b) || IsImpossibleWindow(Flip(op), b, a):
                ReportWindow(a, b, op, span, scope, crossing);
                break;
        }
    }

    private static void CheckRange(BinaryOperator op, BoundFeature feature, double value, string featureText, SourceSpan span, BindingScope scope, bool featureFirst, bool crossing)
    {
        if (feature.Type != BoundType.Number || !ValueRange.Table.TryGetValue(feature.Spec.Name, out var range) || !IsImpossible(op, range, value))
        {
            return;
        }

        // the nearest comparison that can be true: the same direction, inclusive, at the limit, when the limit is a value the feature can reach
        (string Op, double Limit)? nearest = op switch
        {
            BinaryOperator.Greater or BinaryOperator.GreaterOrEqual when double.IsFinite(range.Maximum) => (">=", range.Maximum),
            BinaryOperator.Less or BinaryOperator.LessOrEqual when range.MinimumIncluded && double.IsFinite(range.Minimum) => ("<=", range.Minimum),
            _ => null,
        };
        string? suggestion = null;
        if (nearest is { } near && !crossing)
        {
            var literal = near.Limit.ToString("R", CultureInfo.InvariantCulture);
            suggestion = featureFirst ? featureText + " " + near.Op + " " + literal : literal + " " + (near.Op == ">=" ? "<=" : ">=") + " " + featureText;
        }

        scope.Warning(
            LintCodes.NeverTrue,
            scope.Quoted(span) + " can never be true: " + feature.Spec.Name + " is " + Bounds(range) + ", so the rule never fires."
                + (suggestion is null ? " Remove the condition or change the number." : " The nearest comparison that can be true is " + BindingScope.Quote(suggestion) + "."),
            span,
            suggestion);
    }

    private static string Bounds(ValueRange range)
    {
        var low = double.IsNegativeInfinity(range.Minimum) ? null : Describe.Number(range.Minimum);
        var high = double.IsPositiveInfinity(range.Maximum) ? null : Describe.Number(range.Maximum);
        return (low, high) switch
        {
            (not null, not null) => "between " + low + " and " + high,
            (not null, null) => (range.MinimumIncluded ? "at least " : "more than ") + low,
            _ => "at most " + high,
        };
    }

    /// <summary>Whether <c>x op value</c> is false for every x the feature can take.</summary>
    private static bool IsImpossible(BinaryOperator op, ValueRange range, double value) => op switch
    {
        BinaryOperator.Greater => value >= range.Maximum,
        BinaryOperator.GreaterOrEqual => value > range.Maximum,
        BinaryOperator.Less => value <= range.Minimum,
        BinaryOperator.LessOrEqual => range.MinimumIncluded ? value < range.Minimum : value <= range.Minimum,
        BinaryOperator.Equal => value > range.Maximum || value < range.Minimum || (!range.MinimumIncluded && value <= range.Minimum),
        _ => false,
    };

    private static BinaryOperator Flip(BinaryOperator op) => op switch
    {
        BinaryOperator.Greater => BinaryOperator.Less,
        BinaryOperator.Less => BinaryOperator.Greater,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        _ => op,
    };

    /// <summary>
    /// <c>field op window</c> where the window is <c>highest</c> and the field is never above its source (so <c>&gt;</c> is false: the field is the source, the source is the high, or the field is the low), or the window is
    /// <c>lowest</c> and the field is never below its source (so <c>&lt;</c> is false), on one timeframe.
    /// </summary>
    private static bool IsImpossibleWindow(BinaryOperator op, BoundFeature field, BoundFeature window)
    {
        if (field.Spec.Timeframe != window.Spec.Timeframe || !RawField(field.Spec.Name, out var f) || !Window(window.Spec, out var isHighest, out var source))
        {
            return false;
        }

        return isHighest
            ? op == BinaryOperator.Greater && (f == source || source == PriceSource.High || f == PriceSource.Low)
            : op == BinaryOperator.Less && (f == source || source == PriceSource.Low || f == PriceSource.High);
    }

    private static bool RawField(string name, out PriceSource field)
    {
        field = name switch
        {
            "open" => PriceSource.Open,
            "high" => PriceSource.High,
            "low" => PriceSource.Low,
            "close" => PriceSource.Close,
            _ => PriceSource.Default,
        };
        return field != PriceSource.Default;
    }

    private static bool Window(FeatureSpec spec, out bool isHighest, out PriceSource source)
    {
        isHighest = spec.Name == "highest";
        source = spec.Source;
        if (!isHighest && spec.Name != "lowest")
        {
            return false;
        }

        if (source == PriceSource.Default && IndicatorCatalog.TryGet(spec.Name, out var info))
        {
            source = info.DefaultSource;
        }

        return source is PriceSource.Open or PriceSource.High or PriceSource.Low or PriceSource.Close;
    }

    private static void ReportWindow(BoundFeature left, BoundFeature right, BinaryOperator op, SourceSpan span, BindingScope scope, bool crossing)
    {
        var (leftText, rightText) = (scope.Text(left.Span), scope.Text(right.Span));
        var windowIsRight = right.Spec.Name is "highest" or "lowest";
        var windowText = windowIsRight ? rightText : leftText;
        string? suggestion = null;
        if (!windowText.StartsWith('('))
        {
            var lagged = windowText + "[1]";
            var (a, b) = windowIsRight ? (leftText, lagged) : (lagged, rightText);
            var whole = scope.Text(span);
            suggestion = crossing ? whole[..whole.IndexOf('(', StringComparison.Ordinal)] + "(" + a + ", " + b + ")" : a + " " + OperatorText(op) + " " + b;
        }

        scope.Warning(
            LintCodes.NeverTrue,
            scope.Quoted(span) + " can never be true: the window of " + BindingScope.Quote(windowText) + " includes the current bar, so it already holds the other price."
                + (suggestion is null ? " Compare with the window one bar back, like highest(20)[1]." : " To compare with the previous bars write " + BindingScope.Quote(suggestion) + "."),
            span,
            suggestion);
    }

    private static string OperatorText(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Equal => "==",
        _ => "!=",
    };
}

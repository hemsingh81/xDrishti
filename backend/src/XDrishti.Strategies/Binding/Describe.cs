using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>The words the messages of the binder and the lint use for types, operators and the parameters of a feature, so that the same thing is always called the same way.</summary>
internal static class Describe
{
    /// <summary>A type as a noun phrase: <c>a number</c>, <c>a true/false value</c>.</summary>
    public static string Type(BoundType type) => type switch
    {
        BoundType.Number => "a number",
        BoundType.Bool => "a true/false value",
        BoundType.Time => "the time of day",
        BoundType.Regime => "a market regime",
        BoundType.Text => "a text",
        _ => "a value",
    };

    /// <summary>What to do instead, for the types that exist only next to a comparison; empty for the others.</summary>
    public static string Hint(BoundType type) => type switch
    {
        BoundType.Time => " Compare `time` with a text, like time >= \"09:45\".",
        BoundType.Regime => " Compare it with a regime name, like regime() == \"UP_LOW_VOL\".",
        BoundType.Text => " A text can only be compared with `time` or `regime()`.",
        _ => string.Empty,
    };

    /// <summary>A number as the source would write it: the shortest text that reads back as the same value.</summary>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string Symbol(BinaryOperator op) => op switch
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
    };

    /// <summary>The comparison with its sides swapped: <c>a &lt; b</c> is <c>b &gt; a</c>.</summary>
    public static BinaryOperator Mirror(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => BinaryOperator.Greater,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        BinaryOperator.Greater => BinaryOperator.Less,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        _ => op,
    };

    /// <summary>The feature with its parameters, for a message: <c>rsi(n, source=close)</c>, <c>bb_upper(n, k)</c>, <c>vwap</c>.</summary>
    public static string Signature(IndicatorInfo info)
    {
        var parts = info.Parameters.Select(p => p.Name).ToList();
        if (info.UsesSource)
        {
            parts.Add("source=" + SourceName(info.DefaultSource));
        }

        return parts.Count == 0 ? info.Name : info.Name + "(" + string.Join(", ", parts) + ")";
    }

    /// <summary>What the feature takes, with the catalog's names, ranges and defaults: <c>`rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).</c></summary>
    public static string Usage(IndicatorInfo info)
    {
        var items = info.Parameters
            .Select(p => "`" + p.Name + "` (" + (p.Kind == ParameterKind.Whole ? "a whole number" : "a number") + " from " + Number(p.Min) + " to " + Number(p.Max) + ", default " + Number(p.Default) + ")")
            .ToList();
        if (info.UsesSource)
        {
            items.Add("`source` (close, open, high or low, default " + SourceName(info.DefaultSource) + ")");
        }

        return items.Count == 0
            ? "`" + info.Name + "` takes no arguments."
            : "`" + Signature(info) + "` takes " + (items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]) + ".";
    }

    /// <summary>The timeframes a feature runs on, as the codes the language uses: <c>1m, 5m, 10m</c>.</summary>
    public static string SupportedTimeframes(IndicatorInfo info) => string.Join(", ", info.SupportedTimeframes.Select(t => t.ToCode()));

    private static string SourceName(PriceSource source) => source switch
    {
        PriceSource.Open => "open",
        PriceSource.High => "high",
        PriceSource.Low => "low",
        _ => "close",
    };
}

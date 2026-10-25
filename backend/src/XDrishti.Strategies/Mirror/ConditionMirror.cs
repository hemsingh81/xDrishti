using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// The BUY to SELL mirror of one condition (spec P3-02e): text to text on the syntax tree, by the closed <see cref="MirrorTable"/>. Every expression gets a mirror type (a price, a signed value, an oscillator, a plain
/// quantity); a comparison <c>A op B</c> becomes the one that holds on the inverted market <c>p' = K - p</c> with the constant K cancelled, which is what flips the operator. A shape with no single meaning is a
/// <see cref="Problem"/> that tells the author to write a <c>sell:</c> block. Pure and thread-safe.
/// </summary>
public static class ConditionMirror
{
    /// <summary>Mirrors the text. <paramref name="parameters"/> are the declared parameter names (a name there is a number, not a feature).</summary>
    public static ConditionMirrorResult Mirror(string source, IReadOnlySet<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parsed = ConditionParser.Parse(source);
        return parsed.Tree is { } tree ? Mirror(tree, parameters) : new ConditionMirrorResult(null, null, null, true);
    }

    /// <summary>Mirrors a parsed condition.</summary>
    public static ConditionMirrorResult Mirror(Expression condition, IReadOnlySet<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(parameters);
        var rewriter = new MirrorRewriter(parameters);
        var result = rewriter.Condition(condition);
        if (result is not null)
        {
            return new ConditionMirrorResult(result, ExpressionPrinter.Print(result), null, false);
        }

        return new ConditionMirrorResult(null, null, rewriter.Failure, rewriter.Unreadable);
    }
}

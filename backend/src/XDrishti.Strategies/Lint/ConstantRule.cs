using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// <c>constant_condition</c> (warning): a true/false part of the condition that reads no data, so it has the same value on every bar (<c>1 &lt; 2</c>, <c>crosses_above(1, 2)</c>, a comparison of a parameter with a
/// number). Only literal and parameter constants are seen: <c>rsi(14) &gt; 100</c> for a bounded oscillator is not. The largest such part is reported, once; a bare <c>true</c> or <c>false</c> is reported only when it
/// is the whole condition (<c>x and true</c> is a common way to switch a part off).
/// </summary>
internal static class ConstantRule
{
    public static void Apply(IBoundExpression root, BindingScope scope) => Visit(root, true, scope);

    private static void Visit(IBoundExpression node, bool isRoot, BindingScope scope)
    {
        if (node.Type == BoundType.Bool && BoundTree.IsConstant(node))
        {
            if (node is not BoundBool || isRoot)
            {
                scope.Warning(
                    LintCodes.ConstantCondition,
                    scope.Quoted(node.Span) + " reads no data, so it is the same on every bar.",
                    node.Span);
            }

            return;
        }

        foreach (var child in BoundTree.Children(node))
        {
            Visit(child, false, scope);
        }
    }
}

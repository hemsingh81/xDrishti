namespace XDrishti.Strategies.Binding;

/// <summary>Walking a bound tree: the children of a node, the features it reads in order, and whether it depends on any data. Recursion follows the depth of the tree, which the parser limits.</summary>
internal static class BoundTree
{
    /// <summary>
    /// The operands of the node, left to right (none for a leaf; the bound parts of a <see cref="BoundInvalid"/>). A list, not an iterator: the compiler makes an iterator read the thread's identity, which
    /// the architecture tests forbid in this library.
    /// </summary>
    public static IReadOnlyList<IBoundExpression> Children(IBoundExpression node) => node switch
    {
        BoundNot not => [not.Operand],
        BoundNegate negate => [negate.Operand],
        BoundLogical logical => [logical.Left, logical.Right],
        BoundArithmetic arithmetic => [arithmetic.Left, arithmetic.Right],
        BoundCompare compare => [compare.Left, compare.Right],
        BoundCrossing crossing => [crossing.Left, crossing.Right],
        BoundRun run => [run.Operand],
        BoundBetween between => [between.Value, between.Low, between.High],
        BoundAbs abs => [abs.Operand],
        BoundMinMax minMax => [minMax.Left, minMax.Right],
        BoundInvalid { Parts: { } parts } => parts,
        _ => [],
    };

    /// <summary>The feature reads under the node, in the order they are written (left to right).</summary>
    public static void CollectFeatures(IBoundExpression node, List<BoundFeature> into)
    {
        if (node is BoundFeature feature)
        {
            into.Add(feature);
            return;
        }

        foreach (var child in Children(node))
        {
            CollectFeatures(child, into);
        }
    }

    /// <summary>
    /// Whether the node has the same value on every bar: it reads no feature and not <c>time</c>. A part that could not be bound is not constant (nothing is said about it). A parameter is a number known at compile
    /// time, so it counts as a constant like a literal.
    /// </summary>
    public static bool IsConstant(IBoundExpression node) => node switch
    {
        BoundNumber or BoundBool => true,
        BoundFeature or BoundTimeCompare or BoundInvalid or BoundText or BoundTimeKeyword => false,
        _ => Children(node).All(IsConstant),
    };
}

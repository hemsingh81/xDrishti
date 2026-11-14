namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>Walking a model: the operands of a node and every node and feature leaf below it, written from the model alone (the library's own tree walkers are not used).</summary>
internal static class ModelTree
{
    /// <summary>The operands of the node, left to right (none for a leaf, a literal or a constant).</summary>
    public static IReadOnlyList<Node> Children(Node node) => node switch
    {
        Neg n => [n.Operand],
        Abs a => [a.Operand],
        MinMax m => [m.Left, m.Right],
        Arith a => [a.Left, a.Right],
        Not n => [n.Operand],
        Logic l => [l.Left, l.Right],
        Cmp c => [c.Left, c.Right],
        Crosses c => [c.Left, c.Right],
        Streak s => [s.Operand],
        Between b => [b.Value, b.Low, b.High],
        _ => [],
    };

    /// <summary>The node and every node below it, parents first.</summary>
    public static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in Children(node))
        {
            foreach (var inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Every feature read under the node, left to right.</summary>
    public static IEnumerable<Leaf> Leaves(Node node) => Descendants(node).OfType<Leaf>();
}

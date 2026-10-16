namespace XDrishti.Strategies.Binding;

/// <summary>
/// A node of the bound tree: the syntax tree after every name is resolved to a catalog feature, a parameter value, a builtin or a constant, every node has a <see cref="BoundType"/> and every argument is mapped.
/// The tree is immutable and made only of the sealed records of this namespace (<c>BoundNumber</c>, <c>BoundFeature</c>, <c>BoundCrossing</c> …), so that the compiler and the tests can switch over it
/// exhaustively. It is an interface, not a base class, so that the library keeps its rule that no class is open for inheritance.
/// </summary>
internal interface IBoundExpression
{
    /// <summary>The type of the node.</summary>
    BoundType Type { get; }

    /// <summary>The text the node was read from.</summary>
    SourceSpan Span { get; }
}

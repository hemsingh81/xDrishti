namespace XDrishti.Strategies.Syntax;

/// <summary>An argument of a <see cref="Call"/>: an expression (<see cref="PositionalArgument"/>) or a name given a name (<see cref="NamedArgument"/>).</summary>
public abstract record Argument(SourceSpan Span) : SyntaxNode(Span);

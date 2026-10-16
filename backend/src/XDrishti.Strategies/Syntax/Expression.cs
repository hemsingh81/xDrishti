namespace XDrishti.Strategies.Syntax;

/// <summary>A node that has a value: a literal, a reference, a call, a subscript, or an operator applied to other expressions.</summary>
public abstract record Expression(SourceSpan Span) : SyntaxNode(Span);

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// A node of the syntax tree of a condition. The tree is immutable and every node knows <see cref="Span"/>, the text it was read from (a parenthesised expression covers its parentheses). Two nodes are equal when they
/// have the same shape and the same values: <b>the position in the source is not part of the identity</b>, so the tree read back from the printed form of a tree equals the tree.
/// </summary>
public abstract record SyntaxNode(SourceSpan Span);

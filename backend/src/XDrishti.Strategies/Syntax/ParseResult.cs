namespace XDrishti.Strategies.Syntax;

/// <summary>
/// What <see cref="ConditionParser.Parse"/> returns: the syntax <see cref="Tree"/> when the text is a valid condition, otherwise every problem found, in source order. Exactly one of the two is filled: a tree and
/// no problems, or no tree and at least one error.
/// </summary>
public sealed record ParseResult(Expression? Tree, IReadOnlyList<Problem> Problems)
{
    /// <summary>True when the text parsed and <see cref="Tree"/> is set.</summary>
    public bool IsSuccess => Tree is not null;
}

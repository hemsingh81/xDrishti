namespace XDrishti.Strategies.Syntax;

/// <summary>An infix operator applied to two expressions. Operators of one precedence level group to the left (<c>a - b - c</c> is <c>(a - b) - c</c>); a comparison cannot be chained.</summary>
public sealed record Binary(BinaryOperator Operator, Expression Left, Expression Right, SourceSpan Span) : Expression(Span)
{
    public bool Equals(Binary? other) => other is not null && Operator == other.Operator && Left.Equals(other.Left) && Right.Equals(other.Right);

    public override int GetHashCode() => HashCode.Combine(Operator, Left, Right);
}

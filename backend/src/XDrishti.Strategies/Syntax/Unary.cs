namespace XDrishti.Strategies.Syntax;

/// <summary>A prefix operator applied to an expression: <c>not rsi(14) &gt; 55</c> (which negates the comparison), <c>-x</c>.</summary>
public sealed record Unary(UnaryOperator Operator, Expression Operand, SourceSpan Span) : Expression(Span)
{
    public bool Equals(Unary? other) => other is not null && Operator == other.Operator && Operand.Equals(other.Operand);

    public override int GetHashCode() => HashCode.Combine(Operator, Operand);
}

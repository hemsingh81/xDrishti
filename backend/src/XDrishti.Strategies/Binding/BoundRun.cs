namespace XDrishti.Strategies.Binding;

/// <summary>
/// <c>rising(x, n)</c> (<see cref="IsRising"/>) or <c>falling(x, n)</c>: <c>x[0] &gt; x[1] &gt; … &gt; x[n]</c>, respectively <c>x[0] &lt; x[1] &lt; … &lt; x[n]</c>, <see cref="Count"/> whole steps, each strict, where <c>x</c> is an
/// expression evaluated at the lags 0 to <see cref="Count"/>. <see cref="Count"/> is a whole number of at least 1; a count the source gave above one million is stored as one million (the lint refuses it anyway).
/// Unknown when any of the <c>n + 1</c> values is not ready.
/// </summary>
internal sealed record BoundRun(bool IsRising, IBoundExpression Operand, int Count, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}

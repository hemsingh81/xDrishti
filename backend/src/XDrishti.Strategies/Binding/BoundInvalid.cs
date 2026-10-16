namespace XDrishti.Strategies.Binding;

/// <summary>
/// A part of the condition that could not be bound; its problem is already reported. It stands in the tree so that the rest can still be checked, and no compile with one produces a plan. <see cref="Parts"/> are the
/// operands of the part that were bound (valid or not), so that the lint can still look at them: a condition with a mistake in one place gets the other mistakes in the other places reported with it.
/// <see cref="Lagged"/> is set when the part was a <c>crosses_*</c>, <c>rising</c> or <c>falling</c> whose operands are among the parts, so that the rules about lags still apply to them.
/// </summary>
internal sealed record BoundInvalid(SourceSpan Span, IReadOnlyList<IBoundExpression>? Parts = null, LaggedCall? Lagged = null) : IBoundExpression
{
    public BoundType Type => BoundType.Invalid;
}

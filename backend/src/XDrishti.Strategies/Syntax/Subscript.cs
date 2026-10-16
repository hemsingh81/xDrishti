namespace XDrishti.Strategies.Syntax;

/// <summary>
/// A value some bars ago: <c>close[1]</c>, <c>tf("1h").close[2]</c>. The syntax allows a subscript only after a <see cref="Reference"/> and only a whole number written in digits, optionally with a minus sign
/// (<see cref="Lag"/> may be 0 or negative here; whether it is a usable lag is for the lint). <see cref="LagSpan"/> is the number, for messages about it.
/// </summary>
public sealed record Subscript(Reference Target, int Lag, SourceSpan LagSpan, SourceSpan Span) : Expression(Span)
{
    public bool Equals(Subscript? other) => other is not null && Lag == other.Lag && Target.Equals(other.Target);

    public override int GetHashCode() => HashCode.Combine(Target, Lag);
}

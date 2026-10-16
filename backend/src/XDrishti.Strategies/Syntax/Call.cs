using System.Collections.Immutable;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// A name followed by arguments in parentheses: <c>rsi(14)</c>, <c>crosses_above(close, vwap)</c>, <c>regime()</c>, <c>bb_upper(20, 2)</c>, or a feature of another timeframe, <c>tf("1h").ema(20)</c> (then
/// <see cref="Timeframe"/> is the string). The arguments are positional expressions or named (<c>source=high</c>); the syntax does not check the names or the counts.
/// </summary>
public sealed record Call(Identifier Name, ImmutableArray<Argument> Arguments, SourceSpan Span, StringLiteral? Timeframe = null) : Expression(Span)
{
    public bool Equals(Call? other) =>
        other is not null && Name.Equals(other.Name) && Equals(Timeframe, other.Timeframe) && NodeEquality.SequenceEqual(Arguments, other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Name, Timeframe, NodeEquality.SequenceHash(Arguments));
}

namespace XDrishti.Domain.PairResearch;

/// <summary>The side of a pair trade: <c>long_spread</c> buys Y and sells beta times X, <c>short_spread</c> the reverse.</summary>
public enum PairSide
{
    LongSpread,
    ShortSpread,
}

namespace XDrishti.Domain.Pairs;

/// <summary>The spread position a signal is judged from. <c>LongSpread</c> is long Y and short <c>beta</c> times X; <c>ShortSpread</c> the reverse (value weights are P14-13).</summary>
public enum PairPosition
{
    Flat,
    LongSpread,
    ShortSpread,
}

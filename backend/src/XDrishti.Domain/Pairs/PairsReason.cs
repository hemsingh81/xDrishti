namespace XDrishti.Domain.Pairs;

/// <summary>The closed list of reasons a pairs value is unavailable (spec P14-12). The first member is the value of an uninitialised result, so a <c>default</c> result is never a usable number.</summary>
public enum PairsReason
{
    BadInput,
    TooShort,
    DegenerateX,
    ZeroResidualVariance,
    NotMeanReverting,
    Oscillating,
    ShortWindow,
    FlatWindow,
    ZUnavailable,
}

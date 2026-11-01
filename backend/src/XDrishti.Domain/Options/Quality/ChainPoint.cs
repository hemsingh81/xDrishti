namespace XDrishti.Domain.Options.Quality;

/// <summary>One strike and side of a recorded chain, as the checks see it (paise; a value Dhan did not send is null). <see cref="Flags"/> are the stored flag bits.</summary>
public sealed record ChainPoint(
    DateTimeOffset Ts,
    DateOnly Expiry,
    int StrikePx,
    bool IsCall,
    long SecurityId,
    int UnderlyingPx,
    int? BidPx,
    int? AskPx,
    long? Oi,
    long? Volume,
    double? Iv,
    double? Delta,
    double? Gamma,
    double? Vega,
    short Flags)
{
    /// <summary>Flag bit: the IV or a Greek was not a usable number.</summary>
    public const short BadGreeksBit = 8;

    /// <summary>Flag bit: the bid was above the ask.</summary>
    public const short CrossedBit = 16;

    public bool BadGreeks => (Flags & BadGreeksBit) != 0;

    public bool Crossed => (Flags & CrossedBit) != 0 || (BidPx is { } bid && AskPx is { } ask && bid > ask);

    /// <summary>A bid and an ask, both above zero, not crossed.</summary>
    public bool TwoSided => BidPx is > 0 && AskPx is > 0 && !Crossed;
}

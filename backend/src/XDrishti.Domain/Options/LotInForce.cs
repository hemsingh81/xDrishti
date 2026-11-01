namespace XDrishti.Domain.Options;

/// <summary>The answer of <see cref="LotSizeLookup"/>: a lot and whether it is confirmed against a circular, or unavailable.</summary>
public sealed record LotInForce(bool Available, int LotSize, bool Confirmed, DateOnly? EffectiveFrom, string? Source)
{
    public static LotInForce Unavailable { get; } = new(false, 0, false, null, null);
}

namespace XDrishti.Domain.Pairs;

/// <summary>Builders for <see cref="PairsResult{T}"/> (its constructors are internal so only the core makes one).</summary>
public static class PairsResults
{
    internal static PairsResult<T> Of<T>(T value)
        where T : notnull => new(value);

    internal static PairsResult<T> Unavailable<T>(PairsReason reason)
        where T : notnull => new(reason);
}

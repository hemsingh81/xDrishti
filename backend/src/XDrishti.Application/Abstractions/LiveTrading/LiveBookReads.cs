using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>Helpers for <see cref="LiveBookRead{T}"/>.</summary>
public static class LiveBookReads
{
    /// <summary>An answer with no rows (an empty day).</summary>
    public static LiveBookRead<T> Empty<T>() => new([], 0, 0, new Dictionary<string, int>(), BrokerReadDrops.None);
}

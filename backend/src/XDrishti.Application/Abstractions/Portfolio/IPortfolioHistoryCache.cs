namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// A short-lived cache of computed portfolio histories (60 seconds per key, ADR 0020 / P2-07). Whatever changes the lots, the holdings or the corporate actions drops everything at once through
/// <see cref="Invalidate"/>; the time limit covers what no write announces (new bars). Values are immutable results. A result computed while an invalidation happened is not stored: read <see cref="Version"/> before computing
/// and pass it to <see cref="Set{T}"/>.
/// </summary>
public interface IPortfolioHistoryCache
{
    /// <summary>Changes with every <see cref="Invalidate"/>.</summary>
    long Version { get; }

    bool TryGet<T>(string key, out T? value)
        where T : class;

    /// <summary>Stores the value unless <see cref="Invalidate"/> was called since <paramref name="version"/> was read.</summary>
    void Set<T>(string key, T value, long version)
        where T : class;

    /// <summary>Forgets every entry.</summary>
    void Invalidate();
}

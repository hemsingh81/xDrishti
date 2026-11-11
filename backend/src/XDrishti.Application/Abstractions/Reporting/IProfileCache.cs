namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// A small in-process cache of computed basket profiles (BK-07 Decision 7). The key carries everything the answer depends on, including the newest stored bar date, so a stale entry cannot be found; the cache is bounded and loses nothing when it is emptied.
/// </summary>
public interface IProfileCache
{
    /// <summary>The cached value of the key, or null.</summary>
    T? Get<T>(string key)
        where T : class;

    /// <summary>Stores the value; the oldest entries leave when the cache is full.</summary>
    void Set<T>(string key, T value)
        where T : class;
}

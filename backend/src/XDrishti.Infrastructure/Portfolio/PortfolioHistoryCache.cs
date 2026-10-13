using System.Collections.Concurrent;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The in-process cache of portfolio histories: an entry lives 60 seconds (by <see cref="TimeProvider"/>), at most <see cref="MaxEntries"/> are kept (expired ones go first, then the oldest), and <see cref="Invalidate"/> forgets all. A singleton, safe for concurrent use.
/// </summary>
internal sealed class PortfolioHistoryCache(TimeProvider clock) : IPortfolioHistoryCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public const int MaxEntries = 64;

    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, object Value)> _entries = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    private long _version;

    public long Version => Volatile.Read(ref _version);

    public bool TryGet<T>(string key, out T? value)
        where T : class
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > clock.GetUtcNow() && entry.Value is T typed)
            {
                value = typed;
                return true;
            }

            _entries.TryRemove(new KeyValuePair<string, (DateTimeOffset, object)>(key, entry));
        }

        value = null;
        return false;
    }

    public void Set<T>(string key, T value, long version)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (version != _version)
            {
                return;
            }

            Add(key, value, now);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _version++;
            _entries.Clear();
        }
    }

    private void Add<T>(string key, T value, DateTimeOffset now)
        where T : class
    {
        if (_entries.Count >= MaxEntries)
        {
            foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList())
            {
                _entries.TryRemove(expired, out _);
            }

            while (_entries.Count >= MaxEntries && _entries.OrderBy(e => e.Value.ExpiresAt).Select(e => e.Key).FirstOrDefault() is { } oldest)
            {
                _entries.TryRemove(oldest, out _);
            }
        }

        _entries[key] = (now + Lifetime, value);
    }
}

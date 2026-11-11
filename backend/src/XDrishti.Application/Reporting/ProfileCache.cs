using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The bounded in-memory <see cref="IProfileCache"/> (first in, first out); one per process, thread safe.</summary>
internal sealed class ProfileCache(BasketProfileSettings settings) : IProfileCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public T? Get<T>(string key)
        where T : class
    {
        lock (_gate)
        {
            return _items.TryGetValue(key, out var value) ? value as T : null;
        }
    }

    public void Set<T>(string key, T value)
        where T : class
    {
        lock (_gate)
        {
            if (_items.TryAdd(key, value))
            {
                _order.Enqueue(key);
            }
            else
            {
                _items[key] = value;
            }

            while (_items.Count > settings.CacheEntries && _order.TryDequeue(out var oldest))
            {
                _items.Remove(oldest);
            }
        }
    }
}

namespace XDrishti.Application.Live;

/// <summary>Detects a feed that cannot hold a connection (P2-10 criterion 5): more than <see cref="MaxReconnects"/> reconnect attempts within <see cref="Window"/> is flapping, and attempts then pause for <see cref="Pause"/>.</summary>
internal sealed class ReconnectLimiter
{
    public const int MaxReconnects = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);

    private readonly Queue<DateTimeOffset> _attempts = new();

    /// <summary>How many attempts are inside the window now.</summary>
    public int Count => _attempts.Count;

    /// <summary>Records a reconnect attempt at <paramref name="now"/>; true when there have been more than <see cref="MaxReconnects"/> within <see cref="Window"/> (this one included).</summary>
    public bool Record(DateTimeOffset now)
    {
        _attempts.Enqueue(now);
        while (_attempts.Count > 0 && now - _attempts.Peek() > Window)
        {
            _attempts.Dequeue();
        }

        return _attempts.Count > MaxReconnects;
    }
}

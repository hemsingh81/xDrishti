using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Live;

/// <summary>
/// The feed's figures for its heartbeat (ADR 0024, P2-10 criterion 15): connected, how many instruments are subscribed, the time of the last tick, ticks per second, reconnects today (India date), dropped packets and the duration of
/// the last flush. Written by the receive and flush loops, read by the supervisor; thread safe. Counts only - nothing here can hold a secret.
/// </summary>
public sealed class FeedMetrics(FeedPacketStats? packets = null)
{
    /// <summary>The most response codes the heartbeat carries (the busiest): keeps its JSON small whatever the feed sends.</summary>
    public const int MaxCodesInHeartbeat = 8;

    private readonly object _gate = new();
    private bool _connected;
    private int _subscribed;
    private DateTimeOffset? _lastTickAt;
    private long _ticks;
    private long _ratedTicks;
    private DateTimeOffset? _ratedAt;
    private double _ticksPerSecond;
    private DateOnly _reconnectDay;
    private int _reconnects;
    private long _droppedBase;
    private long _droppedConnection;
    private int _flushMs;

    /// <summary>The messages of the window by response code and outcome (reset when a window starts).</summary>
    public FeedPacketStats Packets { get; } = packets ?? new FeedPacketStats();

    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            _connected = connected;
            if (!connected)
            {
                _subscribed = 0;
            }
        }
    }

    public void SetSubscribed(int subscribed)
    {
        lock (_gate)
        {
            _subscribed = subscribed;
        }
    }

    /// <summary>A tick was taken (its time is the time of the price, not of its arrival).</summary>
    public void RecordTick(DateTimeOffset tickAt)
    {
        lock (_gate)
        {
            _ticks++;
            if (_lastTickAt is null || tickAt > _lastTickAt)
            {
                _lastTickAt = tickAt;
            }
        }
    }

    /// <summary>A reconnect attempt began (counted for the India date of <paramref name="now"/>).</summary>
    public void RecordReconnect(DateTimeOffset now)
    {
        lock (_gate)
        {
            RollDay(now);
            _reconnects++;
        }
    }

    /// <summary>Packets dropped outside the connection (a tick of an instrument that is not in the set).</summary>
    public void RecordDropped(long count)
    {
        lock (_gate)
        {
            _droppedBase += count;
        }
    }

    /// <summary>The running total of the drops of the open connection (it counts its own).</summary>
    public void SetConnectionDropped(long count)
    {
        lock (_gate)
        {
            _droppedConnection = count;
        }
    }

    /// <summary>The connection ended: what it dropped is kept in the total.</summary>
    public void ConnectionEnded()
    {
        lock (_gate)
        {
            _droppedBase += _droppedConnection;
            _droppedConnection = 0;
            _connected = false;
            _subscribed = 0;
        }
    }

    public void SetFlushMs(int milliseconds)
    {
        lock (_gate)
        {
            _flushMs = milliseconds;
        }
    }

    /// <summary>All packets dropped so far (parser drops of every connection and ticks of instruments outside the set).</summary>
    public long Dropped
    {
        get
        {
            lock (_gate)
            {
                return _droppedBase + _droppedConnection;
            }
        }
    }

    /// <summary>The figures now. Ticks per second is the rate since the previous call (the heartbeat's own interval); the first call reports 0.</summary>
    public FeedHeartbeat Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            RollDay(now);
            if (_ratedAt is { } since && now > since)
            {
                _ticksPerSecond = Math.Round((_ticks - _ratedTicks) / (now - since).TotalSeconds, 1);
            }

            _ratedAt = now;
            _ratedTicks = _ticks;
            var byCode = Packets.Summary(MaxCodesInHeartbeat);
            return new FeedHeartbeat(_connected, _subscribed, _lastTickAt, _ticksPerSecond, _reconnects, _droppedBase + _droppedConnection, _flushMs, byCode.Count == 0 ? null : byCode);
        }
    }

    private void RollDay(DateTimeOffset now)
    {
        var today = IndiaTime.Today(now);
        if (today != _reconnectDay)
        {
            _reconnectDay = today;
            _reconnects = 0;
        }
    }
}

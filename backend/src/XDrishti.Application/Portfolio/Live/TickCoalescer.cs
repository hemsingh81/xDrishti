using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Portfolio.Live;

/// <summary>
/// Keeps the latest tick of each instrument between two flushes (ADR 0024 §3). A tick is dropped when its price is not positive, when its tick time is older than the newest one seen for the instrument (a late arrival), or when it has the same time
/// and the same price (a duplicate). Dhan stamps trades with whole epoch seconds, so a tick of the same second with another price is a new tick and counts; otherwise the tick replaces the pending tick of that instrument. <see cref="Drain"/> hands out what changed since the last flush and empties the pending set, so a flush with no new tick has nothing to write.
/// Memory is bounded by the number of instruments. Thread safe: the receive loop adds, the flush loop drains.
/// </summary>
internal sealed class TickCoalescer
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, LivePrice> _newest = [];
    private readonly Dictionary<Guid, LivePrice> _pending = [];

    /// <summary>True when something changed since the last <see cref="Drain"/>.</summary>
    public bool HasChanges
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>Takes a tick; false when it was dropped.</summary>
    public bool Add(LivePrice tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.Price <= 0m)
        {
            return false;
        }

        lock (_gate)
        {
            if (_newest.TryGetValue(tick.InstrumentId, out var newest) && (tick.TickAt < newest.TickAt || (tick.TickAt == newest.TickAt && tick.Price == newest.Price)))
            {
                return false;
            }

            _newest[tick.InstrumentId] = tick;
            _pending[tick.InstrumentId] = tick;
            return true;
        }
    }

    /// <summary>The ticks that changed since the last drain, ordered by instrument id (so that a batch always walks the rows in the same order); the pending set is empty afterwards.</summary>
    public IReadOnlyList<LivePrice> Drain()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            var drained = _pending.Values.OrderBy(t => t.InstrumentId).ToList();
            _pending.Clear();
            return drained;
        }
    }

    /// <summary>Puts ticks back after a failed flush; a newer tick that arrived meanwhile is kept.</summary>
    public void Requeue(IReadOnlyList<LivePrice> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        lock (_gate)
        {
            foreach (var tick in ticks)
            {
                if (!_pending.TryGetValue(tick.InstrumentId, out var pending) || pending.TickAt < tick.TickAt)
                {
                    _pending[tick.InstrumentId] = tick;
                }
            }
        }
    }

    /// <summary>Forgets everything, including the newest times (a new window starts with a clean slate).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _newest.Clear();
            _pending.Clear();
        }
    }
}

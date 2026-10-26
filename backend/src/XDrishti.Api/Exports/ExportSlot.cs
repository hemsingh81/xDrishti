namespace XDrishti.Api.Exports;

/// <summary>
/// The rate rule of the exports (spec P8-20 criterion 10): at most six started in any minute and one at a time. One owner, so one counter for the host. It lives here and not in the rate-limiter middleware so that a refusal because the capability is OFF is always the 403 and never
/// a 429 that hides it.
/// </summary>
internal sealed class ExportSlot(TimeProvider clock)
{
    /// <summary>Exports started in any minute.</summary>
    public const int PerMinute = 6;

    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _started = new();
    private bool _busy;

    /// <summary>Takes the slot, or false when one export is running or six started in the last minute. Release it with <see cref="Leave"/>.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            while (_started.Count > 0 && now - _started.Peek() >= TimeSpan.FromMinutes(1))
            {
                _started.Dequeue();
            }

            if (_busy || _started.Count >= PerMinute)
            {
                return false;
            }

            _busy = true;
            _started.Enqueue(now);
            return true;
        }
    }

    /// <summary>Gives the slot back.</summary>
    public void Leave()
    {
        lock (_gate)
        {
            _busy = false;
        }
    }

    /// <summary>Forgets what was counted (for tests).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _started.Clear();
            _busy = false;
        }
    }
}

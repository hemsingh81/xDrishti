namespace XDrishti.Execution.Application;

/// <summary>
/// Sliding-window limit on sends (writes only; reads are never counted). In memory in this slice, so a restart forgets the counts: the day count from the audit rows is P12-02b. A refused send is not counted.
/// </summary>
public sealed class ExecRateLimiter(TimeProvider clock, int perSecond, int perMinute, int perDay)
{
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _sends = new();

    public bool TryAdmit()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            while (_sends.Count > 0 && now - _sends.Peek() >= TimeSpan.FromDays(1))
            {
                _sends.Dequeue();
            }

            if (_sends.Count >= perDay || _sends.Count(t => now - t < TimeSpan.FromMinutes(1)) >= perMinute || _sends.Count(t => now - t < TimeSpan.FromSeconds(1)) >= perSecond)
            {
                return false;
            }

            _sends.Enqueue(now);
            return true;
        }
    }
}

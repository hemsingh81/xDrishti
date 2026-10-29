namespace XDrishti.Execution.Application;

/// <summary>
/// Sliding-window limit on sends (writes only; reads are never counted). The caps are the constructor values or, in the overload, the values of the settings in force at that moment. <see cref="Seed"/> loads the write-ahead rows of the last day so a
/// restart keeps the day and minute counts (P12-02b). A refused send is not counted.
/// </summary>
public sealed class ExecRateLimiter(TimeProvider clock, int perSecond, int perMinute, int perDay)
{
    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _sends = [];

    public bool IsSeeded { get; private set; }

    public bool TryAdmit() => TryAdmit(perSecond, perMinute, perDay);

    public bool TryAdmit(int secondCap, int minuteCap, int dayCap)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            _sends.RemoveAll(t => now - t >= TimeSpan.FromDays(1));
            if (_sends.Count >= dayCap || _sends.Count(t => now - t < TimeSpan.FromMinutes(1)) >= minuteCap || _sends.Count(t => now - t < TimeSpan.FromSeconds(1)) >= secondCap)
            {
                return false;
            }

            _sends.Add(now);
            return true;
        }
    }

    /// <summary>Adds the given send times to the window, once (the first call wins). The persistent journal supplies them at the first send after a start, before any send is admitted.</summary>
    public void Seed(IEnumerable<DateTimeOffset> sends)
    {
        lock (_gate)
        {
            if (IsSeeded)
            {
                return;
            }

            _sends.AddRange(sends);
            IsSeeded = true;
        }
    }
}

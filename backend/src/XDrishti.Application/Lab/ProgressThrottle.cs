namespace XDrishti.Application.Lab;

/// <summary>At most one pass per interval (spec P5-02, D5: four events a second for a run). One per run; not thread-safe, the run's watcher is its only caller.</summary>
internal sealed class ProgressThrottle(TimeProvider time, TimeSpan interval)
{
    /// <summary>The interval of four events a second.</summary>
    public static readonly TimeSpan QuarterSecond = TimeSpan.FromMilliseconds(250);

    private long _last;

    private bool _any;

    /// <summary>True, and the interval starts again, when the last pass is at least one interval ago (or there was none).</summary>
    public bool TryPass()
    {
        var now = time.GetTimestamp();
        if (_any && time.GetElapsedTime(_last, now) < interval)
        {
            return false;
        }

        _last = now;
        _any = true;
        return true;
    }
}

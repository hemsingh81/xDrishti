using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// A streaming indicator (ADR 0031): it is fed one <b>closed</b> bar at a time, in time order, and keeps constant (or amortised constant) work and memory per bar. It holds no reference to
/// future data, so its value after bar <i>k</i> is a function of the bars <i>1..k</i> only. An instance is thread-confined (no locking).
/// </summary>
/// <remarks>
/// A value that is not ready is <see cref="double.NaN"/> with <see cref="IsReady"/> false; callers treat it as a false condition. <see cref="Reset"/> returns the instance to the state it had after
/// construction. After construction (and warm-up of the JIT) <see cref="Update"/> allocates nothing.
/// </remarks>
public interface IIndicator
{
    /// <summary>The value as of the last bar fed; <see cref="double.NaN"/> while <see cref="IsReady"/> is false.</summary>
    double Value { get; }

    /// <summary>Whether enough bars have been fed for <see cref="Value"/> to be defined.</summary>
    bool IsReady { get; }

    /// <summary>The number of bars after which the indicator is first ready on an unbroken series (session indicators: counted from the first bar of the first session).</summary>
    int WarmUp { get; }

    /// <summary>Feeds the next closed bar. Bars must come in time order.</summary>
    void Update(in Bar bar);

    /// <summary>Forgets every bar fed so far.</summary>
    void Reset();
}

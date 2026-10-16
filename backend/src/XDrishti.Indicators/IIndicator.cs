using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// A streaming indicator (ADR 0031): it is fed one <b>closed</b> bar at a time, in time order, and keeps constant (or amortised constant) work and memory per bar. It holds no reference to
/// future data, so its value after bar <i>k</i> is a function of the bars <i>1..k</i> only — with one family of exceptions that read no more than the past as well: the <b>date-driven</b> features
/// (<see cref="DateFeature"/>, ADR 0033) are a function of the <i>India dates</i> of the bars <i>1..k</i> and of read-only data given at construction (the days of a <see cref="MarketContext"/> strictly before each date, the
/// set of expiry dates), not of the prices of those bars; a later bar or a later context day never changes a value already given. An instance is thread-confined (no locking).
/// </summary>
/// <remarks>
/// A value that is not ready is <see cref="double.NaN"/> with <see cref="IsReady"/> false; callers treat it as a false condition. For a bar-driven indicator readiness is reached after <see cref="WarmUp"/> bars and
/// kept on an unbroken series; for a date-driven one it follows the data and can be lost again (a context day without a VIX makes <c>vix</c> not ready on the dates that read it). <see cref="Reset"/> returns the
/// instance to the state it had after construction. After construction (and warm-up of the JIT) <see cref="Update"/> allocates nothing.
/// </remarks>
public interface IIndicator
{
    /// <summary>The value as of the last bar fed; <see cref="double.NaN"/> while <see cref="IsReady"/> is false.</summary>
    double Value { get; }

    /// <summary>Whether <see cref="Value"/> is defined: enough bars have been fed (a bar-driven indicator), or the data behind the date of the last bar has the value (a date-driven one, which can be not ready again).</summary>
    bool IsReady { get; }

    /// <summary>
    /// The number of bars after which the indicator is first ready on an unbroken series (session indicators: counted from the first bar of the first session). <b>The unit of a date-driven feature is not the bar:</b>
    /// <see cref="DateFeature.WarmUp"/> counts <i>days of the market context</i> that must lie before the date, so a preload for it is sized in daily context records, never in 1-minute bars.
    /// </summary>
    int WarmUp { get; }

    /// <summary>Feeds the next closed bar. Bars must come in time order.</summary>
    void Update(in Bar bar);

    /// <summary>Forgets every bar fed so far.</summary>
    void Reset();
}

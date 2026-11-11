using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// Quote packets to 1-minute bars (BK-03b criterion 2) - pure: no clock, no I/O; the caller gives the exchange time of each packet and, for <see cref="Flush"/>, the time now. A minute is the IST minute of the packet's <b>exchange time</b>, never the receive time.
/// A bar is complete when the first packet of a later minute of the same instrument arrives, or when <see cref="Flush"/> finds the minute over by the grace. A packet with an exchange time before the instrument's current minute, or for a minute already
/// written, is dropped and counted (<see cref="Late"/>); a packet outside [<c>from</c>, <c>until</c>) of the day is counted (<see cref="Outside"/>) and ignored. A minute with no packet gives no bar - nothing is filled forward.
/// </summary>
internal sealed class MinuteBarBuilder(TimeOnly from, TimeOnly until)
{
    private static readonly TimeSpan Ist = new(5, 30, 0);
    private readonly Dictionary<Guid, Open> _open = [];

    /// <summary>Packets dropped because their exchange time was before the instrument's current minute or in a minute already written.</summary>
    public long Late { get; private set; }

    /// <summary>Packets outside the recorded minutes.</summary>
    public long Outside { get; private set; }

    /// <summary>The packet in; the bar it completes, if any (the minute before the packet's, when the packet starts a later one).</summary>
    public MinuteBar? Offer(Guid instrument, DateTimeOffset exchangeTime, decimal price, long cumulativeVolume)
    {
        var local = exchangeTime.ToOffset(Ist);
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
        if (time < from || time >= until)
        {
            Outside++;
            return null;
        }

        var minute = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, Ist);
        if (!_open.TryGetValue(instrument, out var current))
        {
            _open[instrument] = new Open(minute, price, 0, cumulativeVolume);
            return null;
        }

        if (minute < current.Minute || (minute == current.Minute && current.Written))
        {
            Late++;
            return null;
        }

        if (minute == current.Minute)
        {
            current.Add(price, cumulativeVolume);
            return null;
        }

        var done = current.Written ? null : current.ToBar(instrument);
        _open[instrument] = new Open(minute, price, current.LastVolume, cumulativeVolume);
        return done;
    }

    /// <summary>The bars of minutes that are over by <paramref name="grace"/> at <paramref name="now"/> and have no later packet yet.</summary>
    public IReadOnlyList<MinuteBar> Flush(DateTimeOffset now, TimeSpan grace)
    {
        var done = new List<MinuteBar>();
        foreach (var (instrument, current) in _open)
        {
            if (!current.Written && now >= current.Minute.AddMinutes(1) + grace)
            {
                done.Add(current.ToBar(instrument));
                current.Written = true;
            }
        }

        return done;
    }

    private sealed class Open
    {
        private readonly decimal _open;
        private readonly long _volumeBefore;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private int _packets = 1;

        public Open(DateTimeOffset minute, decimal price, long volumeBefore, long cumulative)
        {
            Minute = minute;
            _open = _high = _low = _close = price;
            _volumeBefore = volumeBefore;
            LastVolume = cumulative;
        }

        public DateTimeOffset Minute { get; }

        public long LastVolume { get; private set; }

        /// <summary>The bar of this minute was handed out by <see cref="Flush"/>.</summary>
        public bool Written { get; set; }

        public void Add(decimal next, long cumulativeVolume)
        {
            _high = Math.Max(_high, next);
            _low = Math.Min(_low, next);
            _close = next;
            _packets++;
            LastVolume = cumulativeVolume;
        }

        public MinuteBar ToBar(Guid instrument) => new(instrument, Minute, _open, _high, _low, _close, Math.Max(0, LastVolume - _volumeBefore), _packets);
    }
}

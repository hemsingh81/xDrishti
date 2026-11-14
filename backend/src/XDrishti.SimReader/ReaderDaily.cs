namespace XDrishti.SimReader;

/// <summary>One session as a daily bar built from its 1-minute bars (the doc 11 frame rule: the close is the close of the last minute that holds a trade, the volume the sum of the minutes).</summary>
internal sealed record ReaderDaily(DateOnly Date, long LastBarEpoch, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume)
{
    public static decimal CloseOf(List<ReaderBar> sessionBars)
    {
        for (var i = sessionBars.Count - 1; i >= 0; i--)
        {
            if (sessionBars[i].Volume > 0m)
            {
                return sessionBars[i].Close;
            }
        }

        return sessionBars[^1].Close;
    }

    /// <summary>Null for a session without a minute.</summary>
    public static ReaderDaily? Of(ReaderSession session)
    {
        var bars = ReaderClock.SessionBars(session.Bars);
        return bars.Count == 0
            ? null
            : new ReaderDaily(session.Date, bars[^1].EpochSeconds, bars[0].Open, bars.Max(b => b.High), bars.Min(b => b.Low), CloseOf(bars), bars.Sum(b => b.Volume));
    }
}

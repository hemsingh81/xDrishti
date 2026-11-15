using XDrishti.Domain.Market;

namespace XDrishti.Application.Replay.Sim;

/// <summary>
/// The pure part of the SR-04 tape (ADR 0293): the trade events of a day at their own minute and the live board at a clock minute. Only what is at or before the minute is used, so the board of minute m never reads a later bar or event; the frontend mirrors <see cref="BoardAt"/>.
/// </summary>
public static class SimTape
{
    /// <summary>The kind of an entry event.</summary>
    public const string EntryKind = "entry";

    /// <summary>The kind of an exit event.</summary>
    public const string ExitKind = "exit";

    /// <summary>The first minute of the NSE session (09:15 India time) of <paramref name="day"/>.</summary>
    public static DateTimeOffset MarketOpen(DateOnly day) => At(day, 9, 15);

    /// <summary>The start of the last minute of the NSE session (15:29 India time) of <paramref name="day"/>.</summary>
    public static DateTimeOffset MarketClose(DateOnly day) => At(day, 15, 29);

    /// <summary>The start of the minute of <paramref name="at"/>.</summary>
    public static DateTimeOffset Minute(DateTimeOffset at) => DateTimeOffset.FromUnixTimeSeconds(at.ToUnixTimeSeconds() / 60 * 60).ToOffset(IndiaTime.Offset);

    /// <summary>The entry and exit events on <paramref name="day"/> of <paramref name="trades"/>, in minute order (entries before exits of the same minute, then by trade number).</summary>
    public static IReadOnlyList<SimTapeEvent> Events(DateOnly day, IEnumerable<SimDayTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var events = new List<SimTapeEvent>();
        foreach (var t in trades)
        {
            if (t.EntrySession == day)
            {
                events.Add(new SimTapeEvent(Minute(t.EntryAt), EntryKind, t.TradeNo, t.Symbol, t.Side, t.Qty, t.EntryPrice, t.StopPrice, null, null));
            }

            if (t.ExitSession == day && t.ExitAt is { } exitAt)
            {
                events.Add(new SimTapeEvent(Minute(exitAt), ExitKind, t.TradeNo, t.Symbol, t.Side, t.Qty, t.ExitPrice, t.StopPrice, t.GrossPnl ?? 0, t.ExitReason));
            }
        }

        return [.. events.OrderBy(e => e.At).ThenBy(e => e.Kind == ExitKind).ThenBy(e => e.TradeNo)];
    }

    /// <summary>
    /// The live board at <paramref name="minute"/>: a trade is open once its entry minute is at or before the minute and until its exit minute is; its last price is the close of the last bar of its symbol at or before the minute. <paramref name="trades"/> are the trades held into or traded on the day;
    /// <paramref name="bars"/> may hold later bars (they are ignored).
    /// </summary>
    public static SimBoard BoardAt(DateTimeOffset minute, decimal equityOpen, IEnumerable<SimDayTrade> trades, IEnumerable<SimTapeBar> bars)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(bars);
        var last = new Dictionary<string, (DateTimeOffset At, double Close)>(StringComparer.Ordinal);
        foreach (var b in bars)
        {
            if (b.At <= minute && (!last.TryGetValue(b.Symbol, out var seen) || b.At > seen.At))
            {
                last[b.Symbol] = (b.At, b.Close);
            }
        }

        var open = new List<SimBoardPosition>();
        var realised = 0m;
        foreach (var t in trades.OrderBy(t => t.TradeNo))
        {
            if (Minute(t.EntryAt) > minute)
            {
                continue;
            }

            if (t.ExitAt is { } exitAt && Minute(exitAt) <= minute)
            {
                realised += IndiaTime.Today(exitAt) == IndiaTime.Today(minute) ? t.GrossPnl ?? 0 : 0; // an exit of an earlier day is in the equity at the open already
                continue;
            }

            decimal? price = last.TryGetValue(t.Symbol, out var l) ? (decimal)l.Close : null;
            var sign = string.Equals(t.Side, "SELL", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
            var pnl = price is { } p ? (p - t.EntryPrice) * t.Qty * sign : 0;
            open.Add(new SimBoardPosition(t.TradeNo, t.Symbol, t.Side, t.Qty, t.EntryAt, t.EntryPrice, t.StopPrice, price, pnl));
        }

        return new SimBoard(minute, open, realised, open.Sum(p => p.RunningPnl), equityOpen, equityOpen + realised);
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), IndiaTime.Offset);
}

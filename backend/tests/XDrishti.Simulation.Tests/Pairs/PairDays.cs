using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Pairs;

/// <summary>
/// The synthetic days of the pair walker tests: dense flat minutes (open = high = low = close, volume 100) where each 5-minute bucket has one price per leg. X is always 100.00. Five prior sessions (2026-06-01 to 05) carry a spread of plus and minus 0.004 by bucket parity (the look-back of
/// the z-score, sd close to 0.004); the trading day is 2026-06-08. The hedge ratio is alpha 0, beta 1, the tick 0.05, the fills have no slippage and no entry limit margin so that every price is the exact one of the table.
/// </summary>
internal static class PairDays
{
    public static readonly DateOnly TradingDay = new(2026, 6, 8);

    public static readonly DateOnly[] PriorDays = [new(2026, 6, 1), new(2026, 6, 2), new(2026, 6, 3), new(2026, 6, 4), new(2026, 6, 5)];

    public static readonly FillRules Exact = FillRules.Create(0, SlippageConfig.None).Value;

    public static readonly TickSpec Tick = TickSpec.FromRupees(0.05m).Value;

    public static readonly SelectedPair Pair = new("NSE:Y", "NSE:X", 0.0, 1.0, 0.0, 5.0, -4.0, 0.001);

    public static long Midnight(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds();

    public static long Epoch(DateOnly day, int minuteOfDay) => Midnight(day) + (minuteOfDay * 60L);

    /// <summary>The bars of a session: a minute from 09:15 to 15:29 unless <paramref name="absent"/> says no; the price of a minute is that of its bucket.</summary>
    public static List<Bar> Session(DateOnly day, Func<int, double> priceOfBucket, Func<int, bool>? absent = null)
    {
        var bars = new List<Bar>();
        for (var minute = SessionClock.OpenMinute; minute < SessionClock.CloseMinute; minute++)
        {
            if (absent?.Invoke(minute) == true)
            {
                continue;
            }

            var p = priceOfBucket((minute - SessionClock.OpenMinute) / 5);
            bars.Add(new Bar(Epoch(day, minute), p, p, p, p, 100));
        }

        return bars;
    }

    /// <summary>The X leg: 100.00 all day on every session; <paramref name="absentOnTradingDay"/> removes minutes of the trading day only.</summary>
    public static List<Bar> XBars(Func<int, bool>? absentOnTradingDay = null) =>
        [.. PriorDays.SelectMany(d => Session(d, _ => 100.0)), .. Session(TradingDay, _ => 100.0, absentOnTradingDay)];

    /// <summary>The Y leg: the prior sessions alternate 100 e^{+0.004} and 100 e^{-0.004} by bucket; the trading day follows <paramref name="tradingDay"/>.</summary>
    public static List<Bar> YBars(Func<int, double> tradingDay, int priorSessions = 5) =>
        [.. PriorDays.Skip(5 - priorSessions).SelectMany(d => Session(d, b => 100.0 * Math.Exp(b % 2 == 0 ? 0.004 : -0.004))), .. Session(TradingDay, tradingDay)];

    /// <summary>Y is 100.00 until bucket <paramref name="from"/>, then <paramref name="high"/> until bucket <paramref name="until"/>, then <paramref name="after"/>.</summary>
    public static Func<int, double> Excursion(int from, double high, int until, double after) => b => b < from ? 100.0 : b < until ? high : after;

    public static PairWalkInput Input(List<Bar> y, List<Bar> x, PairVariant? variant = null, FillRules? fills = null, PairStudyOptions? options = null) =>
        new(Pair, y, x, Tick, Tick, variant ?? PairVariant.Primary, options ?? PairStudyOptions.Default, fills ?? Exact, TradingDay, TradingDay);

    /// <summary>The mean and sample sd of the spread values of the five prior sessions, as the walker's window.</summary>
    public static (double Mean, double Sd) Window()
    {
        var values = PriorDays.SelectMany(_ => Enumerable.Range(0, 75).Select(b => Math.Log(100.0 * Math.Exp(b % 2 == 0 ? 0.004 : -0.004)) - Math.Log(100.0))).ToArray();
        var mean = values.Average();
        return (mean, Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1)));
    }
}

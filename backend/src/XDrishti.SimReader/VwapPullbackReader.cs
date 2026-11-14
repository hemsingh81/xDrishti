namespace XDrishti.SimReader;

/// <summary>
/// Reads one VWAP_PULLBACK trade off one India trading day of 1-minute bars plus the sessions before it, from the rule tables of the strategy spec (P3-05 D1 to D6) and ADR 0037, 0038 only.
/// Written without any engine, strategy or indicator code. Pure, decimal, no clock. Returns null when the rules give no trade.
/// The trend setup needs history: 20 daily bars and 50 hourly bars (7 per session, the 15:15 stub included); with less the setup is not ready and there is no trade.
/// </summary>
public static class VwapPullbackReader
{
    private const int AtrPeriod = 14;
    private const int FastEma = 20;
    private const int SlowEma = 50;
    private const int Evaluation = 15;
    private const int Hour = 60;

    public static ReaderReading? Read(IReadOnlyList<ReaderSession> priorSessions, IReadOnlyList<ReaderBar> day, VwapPullbackStrategy strategy, ReaderSide side)
    {
        ArgumentNullException.ThrowIfNull(priorSessions);
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(strategy);
        if (strategy.Tick <= 0m || strategy.RewardRisk <= 0m || strategy.PullAtr <= 0m || strategy.EntryLimitPercent < 0m)
        {
            throw new ArgumentException("tick, reward-risk and pull_atr must be above 0 and the limit percent not negative.", nameof(strategy));
        }

        var bars = ReaderClock.SessionBars(day);
        if (bars.Count == 0)
        {
            return null;
        }

        // history: the 15-minute bars, the hourly closes and the daily closes of the sessions before today
        var priorBuckets = new List<Bucket>();
        var priorHourly = new List<decimal>();
        var priorDaily = new List<decimal>();
        foreach (var session in priorSessions)
        {
            var sessionBars = ReaderClock.SessionBars(session.Bars);
            if (sessionBars.Count == 0)
            {
                continue;
            }

            priorBuckets.AddRange(ReaderBuckets.Build(sessionBars, Evaluation));
            var sessionHourly = ReaderBuckets.Build(sessionBars, Hour);
            priorHourly.AddRange(ReaderBuckets.Closes(sessionBars, sessionHourly, Hour, false).Select(c => sessionHourly[c.Bucket].Close)); // the bars that closed, as for today's below
            priorDaily.Add(ReaderDaily.CloseOf(sessionBars));
        }

        var buckets = ReaderBuckets.Build(bars, Evaluation);
        var closes = ReaderBuckets.Closes(bars, buckets, Evaluation);
        var hourly = ReaderBuckets.Build(bars, Hour);
        var hourlyCloses = ReaderBuckets.Closes(bars, hourly, Hour, false);
        var atr = Atr(priorBuckets, buckets);
        var vwap = Vwap(buckets);
        var buy = side == ReaderSide.Buy;

        foreach (var (b, closing) in closes)
        {
            var decisionMinute = ReaderClock.MinuteOfDay(bars[closing]) + 1;
            if (decisionMinute < ReaderClock.Minutes(strategy.FirstEntry) || decisionMinute > ReaderClock.Minutes(strategy.LastEntry) || b < 2)
            {
                continue;
            }

            if (!Triggers(buckets, vwap, atr[priorBuckets.Count + b], b, buy, strategy.PullAtr) || !SetupHolds(priorHourly, priorDaily, hourly, hourlyCloses, closing, buy))
            {
                continue;
            }

            var trigger = buckets[b];
            var stop = buy ? Math.Min(buckets[b - 1].Low, trigger.Low) : Math.Max(buckets[b - 1].High, trigger.High); // lowest(low, 2) / highest(high, 2), used as given
            if (ReaderWalk.IntradayFill(bars, buy, trigger.Close, closing, stop, strategy.Tick, strategy.EntryLimitPercent, ReaderClock.Minutes(strategy.SquareOff)) is not { } fill)
            {
                return null; // the first candidate of the day is the only one: a miss is final
            }

            var risk = Math.Abs(fill.Price - stop);
            var direction = buy ? 1m : -1m;
            var target = fill.Price + (direction * strategy.RewardRisk * risk);
            var (exitIndex, exitPrice, reason) = ReaderWalk.Exit(bars, buy, fill.Index, fill.AtLimit, stop, target, ReaderClock.Minutes(strategy.SquareOff), null);
            return new ReaderReading(
                0m, 0m, trigger.StartEpoch, trigger.Close, bars[fill.Index].EpochSeconds, fill.Price, fill.AtLimit, stop, target, bars[exitIndex].EpochSeconds, exitPrice, reason, Math.Round(direction * (exitPrice - fill.Price) / risk, 6));
        }

        return null;
    }

    private static bool Triggers(List<Bucket> buckets, decimal?[] vwap, decimal? atr, int b, bool buy, decimal pullAtr)
    {
        if (vwap[b] is not { } v0 || vwap[b - 1] is not { } v1 || vwap[b - 2] is not { } v2 || atr is not { } a)
        {
            return false;
        }

        var (t, d, e) = (buckets[b], buckets[b - 1], buckets[b - 2]);
        return buy
            ? t.Close > v0 && t.Close > t.Open && d.Close > v1 && e.Close > v2 && d.Low < e.Low && d.Low <= v1 + (pullAtr * a)
            : t.Close < v0 && t.Close < t.Open && d.Close < v1 && e.Close < v2 && d.High > e.High && d.High >= v1 - (pullAtr * a);
    }

    /// <summary>The hourly trend (last closed hourly bar, the bar closed at this very step included) and the daily trend (the previous session's close against its EMA).</summary>
    private static bool SetupHolds(List<decimal> priorHourly, List<decimal> priorDaily, List<Bucket> hourly, List<(int Bucket, int Closing)> hourlyCloses, int closing, bool buy)
    {
        var hourlyValues = new List<decimal>(priorHourly);
        hourlyValues.AddRange(hourlyCloses.Where(c => c.Closing <= closing).Select(c => hourly[c.Bucket].Close));
        if (ReaderMath.Ema(hourlyValues, FastEma) is not { } fast || ReaderMath.Ema(hourlyValues, SlowEma) is not { } slow || ReaderMath.Ema(priorDaily, FastEma) is not { } daily)
        {
            return false;
        }

        var lastDaily = priorDaily[^1];
        return buy ? fast > slow && lastDaily > daily : fast < slow && lastDaily < daily;
    }

    /// <summary>Wilder's average of the true range over the 15-minute bars of all sessions in a row, seeded with the mean of the first 14 true ranges; one entry per prior bar then per bar of today.</summary>
    private static decimal?[] Atr(List<Bucket> prior, List<Bucket> today)
    {
        var all = prior.Concat(today).ToList();
        var result = new decimal?[all.Count];
        var sum = 0m;
        decimal average = 0m;
        for (var i = 0; i < all.Count; i++)
        {
            var range = all[i].High - all[i].Low;
            var tr = i == 0 ? range : Math.Max(range, Math.Max(Math.Abs(all[i].High - all[i - 1].Close), Math.Abs(all[i].Low - all[i - 1].Close)));
            if (i < AtrPeriod)
            {
                sum += tr;
                if (i == AtrPeriod - 1)
                {
                    average = sum / AtrPeriod;
                    result[i] = average;
                }
            }
            else
            {
                average = (((AtrPeriod - 1) * average) + tr) / AtrPeriod;
                result[i] = average;
            }
        }

        return result;
    }

    /// <summary>The volume-weighted mean of the 15-minute bars' typical prices (H + L + C) / 3 since 09:15 as each bar closes; null while no volume has traded.</summary>
    private static decimal?[] Vwap(List<Bucket> buckets)
    {
        var result = new decimal?[buckets.Count];
        var priceVolume = 0m;
        var volume = 0m;
        for (var i = 0; i < buckets.Count; i++)
        {
            priceVolume += (buckets[i].High + buckets[i].Low + buckets[i].Close) / 3m * buckets[i].Volume;
            volume += buckets[i].Volume;
            result[i] = volume > 0m ? priceVolume / volume : null;
        }

        return result;
    }
}

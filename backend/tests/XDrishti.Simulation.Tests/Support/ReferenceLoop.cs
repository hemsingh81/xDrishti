using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The plain reference loop of VWAP_PULLBACK (spec P3-05, D12), written from the rule text of D1 to D6 and the bucket rule of D2 only. It shares no production helper: it uses the minute <see cref="Bar"/> and the <see cref="Side"/> of the domain and
/// nothing from the indicator, strategy or simulation libraries (a test reads this file and checks it). Everything is plain loops over <c>decimal</c>: the 15-minute bars from the minutes, the hourly bars (seven a session: buckets from 09:15, 10:15 ... 14:15
/// of 60 minutes and the stub from 15:15 of 15), the daily bars, the volume-weighted VWAP of the session, the Wilder ATR(14) seeded with the mean of the first 14 true ranges, the SMA-seeded EMAs; then the setup, the window, the three trigger items, the first
/// candidate of a side and a date, and a minute-by-minute walk with the limit, stop and target rules of D6 and no slippage.
/// </summary>
internal static class ReferenceLoop
{
    private const int OpenMinute = 9 * 60 + 15;

    private const int Tick = 5;

    /// <summary>The mutants the control uses: a rule text read wrongly, to show the comparison with the engine can fail.</summary>
    public enum Mutant
    {
        None,
        VwapLagReadAsCurrent,
        LowLaggedByOne,
        HourlyStubDropped,
    }

    /// <summary>A candidate: the side, the list index of the minute that closes the trigger bar, and the stop.</summary>
    public sealed record Entry(Side Side, int DecisionIndex, decimal Stop);

    /// <summary>A trade of the loop.</summary>
    public sealed record Trade(Side Side, int DecisionIndex, long EntryEpoch, decimal EntryPrice, decimal Stop, decimal Target, long ExitEpoch, decimal ExitPrice, string Reason, decimal R);

    /// <summary>What the loop finds on a list of minutes: every candidate, the trade or the absence of one for each, and per India date and condition path the number of window steps at which the condition was true and false.</summary>
    public sealed record Found(IReadOnlyList<Entry> Candidates, IReadOnlyList<Trade> Trades, IReadOnlyDictionary<(DateOnly Date, string Path), (int True, int False)> Counts);

    private sealed record Minute(long Epoch, int Day, int Clock, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    private sealed class Bar15
    {
        public int Day { get; init; }

        public int Number { get; init; }

        public int FirstClock { get; init; }

        public int LastIndex { get; init; }

        public long EndEpoch { get; init; }

        public decimal Open { get; init; }

        public decimal High { get; init; }

        public decimal Low { get; init; }

        public decimal Close { get; init; }

        public decimal Volume { get; init; }

        public decimal Vwap { get; set; }

        public decimal? Atr { get; set; }
    }

    /// <summary>Runs the loop over the minutes (ascending, one instrument, tick 0.05, the 0.1 % limit, no slippage).</summary>
    public static Found Run(IReadOnlyList<Bar> bars, double pullAtr = 0.5, double rewardRisk = 2.0, Mutant mutant = Mutant.None)
    {
        var minutes = Minutes(bars);
        var bars15 = Build15(minutes);
        FillVwapAndAtr(bars15);
        var hourly = BuildHourly(minutes, mutant == Mutant.HourlyStubDropped);
        var daily = BuildDaily(minutes);
        var pull = Dec(pullAtr);
        var rr = Dec(rewardRisk);

        var hourlyFast = Ema(hourly.Select(h => h.Close).ToList(), 20);
        var hourlySlow = Ema(hourly.Select(h => h.Close).ToList(), 50);
        var dailyEma = Ema(daily.Select(d => d.Close).ToList(), 20);

        var candidates = new List<Entry>();
        var trades = new List<Trade>();
        var counts = new Dictionary<(DateOnly, string), (int, int)>();
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var sign = side == Side.Buy ? 1 : -1;
            var name = side == Side.Buy ? "buy" : "sell";
            var doneDays = new HashSet<int>();
            for (var j = 0; j < bars15.Count; j++)
            {
                var bar = bars15[j];
                if (bar.Number < 3 || j < 2 || bars15[j - 1].Day != bar.Day || bars15[j - 2].Day != bar.Day)
                {
                    continue;
                }

                var decisionClock = bar.FirstClock + 15;
                if (decisionClock < 10 * 60 || decisionClock > 14 * 60 + 30 || bar.Atr is null)
                {
                    continue;
                }

                var closedHourly = hourly.Count(h => h.EndEpoch <= bar.EndEpoch);
                var closedDaily = daily.Count(d => d.Day < bar.Day);
                if (closedHourly < 50 || closedDaily < 20)
                {
                    continue;
                }

                var fast = hourlyFast[closedHourly - 1]!.Value;
                var slow = hourlySlow[closedHourly - 1]!.Value;
                var dailyClose = daily[closedDaily - 1].Close;
                var dailyAverage = dailyEma[closedDaily - 1]!.Value;
                var items = new[]
                {
                    side == Side.Buy ? fast > slow : fast < slow,
                    side == Side.Buy ? dailyClose > dailyAverage : dailyClose < dailyAverage,
                    TriggerItem(bars15, j, side, pull, mutant, 0),
                    TriggerItem(bars15, j, side, pull, mutant, 1),
                    TriggerItem(bars15, j, side, pull, mutant, 2),
                };
                var date = DateOnly.FromDayNumber(DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber + bar.Day);
                for (var n = 0; n < items.Length; n++)
                {
                    var key = (date, name + (n < 2 ? ".setup[" + n : ".trigger[" + (n - 2)) + "]");
                    var (t, f) = counts.GetValueOrDefault(key);
                    counts[key] = items[n] ? (t + 1, f) : (t, f + 1);
                }

                if (doneDays.Contains(bar.Day) || !items.All(i => i))
                {
                    continue;
                }

                var stop = side == Side.Buy ? Math.Min(bar.Low, bars15[j - 1].Low) : Math.Max(bar.High, bars15[j - 1].High);
                candidates.Add(new Entry(side, bar.LastIndex, stop));
                doneDays.Add(bar.Day);
                if (Walk(minutes, bar.LastIndex, side, bar.Close, stop, rr, sign) is { } trade)
                {
                    trades.Add(trade);
                }
            }
        }

        return new Found(
            [.. candidates.OrderBy(c => c.DecisionIndex).ThenBy(c => c.Side)],
            [.. trades.OrderBy(t => t.DecisionIndex).ThenBy(t => t.Side)],
            counts.ToDictionary(c => c.Key, c => c.Value));
    }

    /// <summary>The trend values at the end of the list: the hourly EMA(20) and EMA(50) of the last closed hourly bar, the last daily close and the daily EMA(20) (for the plain-arithmetic checks of the fixtures).</summary>
    public static (decimal HourlyFast, decimal HourlySlow, decimal DailyClose, decimal DailyEma) TrendAtEnd(IReadOnlyList<Bar> bars, bool dropStub = false)
    {
        var minutes = Minutes(bars);
        var hourly = BuildHourly(minutes, dropStub);
        var daily = BuildDaily(minutes);
        var fast = Ema(hourly.Select(h => h.Close).ToList(), 20);
        var slow = Ema(hourly.Select(h => h.Close).ToList(), 50);
        var dailyEma = Ema(daily.Select(d => d.Close).ToList(), 20);
        return (fast[^1]!.Value, slow[^1]!.Value, daily[^1].Close, dailyEma[^1]!.Value);
    }

    private static bool TriggerItem(List<Bar15> b, int j, Side side, decimal pull, Mutant mutant, int item)
    {
        var bar = b[j];
        var vwap1 = mutant == Mutant.VwapLagReadAsCurrent ? b[j].Vwap : b[j - 1].Vwap;
        var vwap2 = b[j - 2].Vwap;
        var atr = bar.Atr!.Value;
        if (side == Side.Buy)
        {
            var lowOne = mutant == Mutant.LowLaggedByOne ? b[j - 2].Low : b[j - 1].Low;
            var lowTwo = mutant == Mutant.LowLaggedByOne ? (j >= 3 && b[j - 3].Day == bar.Day ? b[j - 3].Low : decimal.MaxValue) : b[j - 2].Low;
            return item switch
            {
                0 => bar.Close > bar.Vwap && bar.Close > bar.Open,
                1 => b[j - 1].Close > vwap1 && b[j - 2].Close > vwap2,
                _ => lowOne < lowTwo && lowOne <= vwap1 + pull * atr,
            };
        }

        var highOne = mutant == Mutant.LowLaggedByOne ? b[j - 2].High : b[j - 1].High;
        var highTwo = mutant == Mutant.LowLaggedByOne ? (j >= 3 && b[j - 3].Day == bar.Day ? b[j - 3].High : decimal.MinValue) : b[j - 2].High;
        return item switch
        {
            0 => bar.Close < bar.Vwap && bar.Close < bar.Open,
            1 => b[j - 1].Close < vwap1 && b[j - 2].Close < vwap2,
            _ => highOne > highTwo && highOne >= vwap1 - pull * atr,
        };
    }

    private static Trade? Walk(List<Minute> minutes, int decisionIndex, Side side, decimal triggerClose, decimal stop, decimal rr, int sign)
    {
        var decisionEpoch = minutes[decisionIndex].Epoch + 60;
        var fillIndex = decisionIndex + 1;
        if (fillIndex >= minutes.Count || minutes[fillIndex].Epoch < decisionEpoch || minutes[fillIndex].Epoch - decisionEpoch > 5 * 60 || minutes[fillIndex].Day != minutes[decisionIndex].Day || minutes[fillIndex].Clock >= 15 * 60 + 15)
        {
            return null;
        }

        var limit = side == Side.Buy ? Math.Floor(triggerClose * 1.001m * 100 / Tick) * Tick / 100 : Math.Ceiling(triggerClose * 0.999m * 100 / Tick) * Tick / 100;
        var fill = minutes[fillIndex];
        decimal entry;
        bool atLimit;
        if (side == Side.Buy ? fill.Open <= limit : fill.Open >= limit)
        {
            entry = fill.Open;
            atLimit = false;
        }
        else if (side == Side.Buy ? fill.Low <= limit : fill.High >= limit)
        {
            entry = limit;
            atLimit = true;
        }
        else
        {
            return null;
        }

        if (side == Side.Buy ? entry <= stop : entry >= stop)
        {
            return null;
        }

        var risk = Math.Abs(entry - stop);
        var target = entry + sign * rr * risk;
        for (var i = fillIndex; i < minutes.Count && minutes[i].Day == fill.Day; i++)
        {
            var m = minutes[i];
            if (m.Volume == 0)
            {
                continue;
            }

            if (m.Clock >= 15 * 60 + 15)
            {
                var beyondStop = side == Side.Buy ? m.Open < stop : m.Open > stop;
                var beyondTarget = side == Side.Buy ? m.Open > target : m.Open < target;
                return beyondStop ? Done(m, m.Open, "Stop") : beyondTarget ? Done(m, target, "Target") : Done(m, m.Open, "SquareOff");
            }

            var stopTouched = side == Side.Buy ? m.Low <= stop : m.High >= stop;
            var targetTouched = !(i == fillIndex && atLimit) && (side == Side.Buy ? m.High >= target : m.Low <= target);
            var openBeyondTarget = side == Side.Buy ? m.Open > target : m.Open < target;
            if (stopTouched && targetTouched)
            {
                return openBeyondTarget ? Done(m, target, "Target") : Done(m, side == Side.Buy ? Math.Min(stop, m.Open) : Math.Max(stop, m.Open), "Stop");
            }

            if (stopTouched)
            {
                return Done(m, side == Side.Buy ? Math.Min(stop, m.Open) : Math.Max(stop, m.Open), "Stop");
            }

            if (targetTouched)
            {
                return Done(m, target, "Target");
            }
        }

        return null;

        Trade Done(Minute m, decimal price, string reason) =>
            new(side, decisionIndex, fill.Epoch, entry, stop, target, m.Epoch, price, reason, sign * (price - entry) / risk);
    }

    private static List<Minute> Minutes(IReadOnlyList<Bar> bars)
    {
        var offset = 330 * 60L;
        return [.. bars.Select(b =>
        {
            var local = b.EpochSeconds + offset;
            return new Minute(b.EpochSeconds, (int)(local / 86_400), (int)(local % 86_400 / 60), Dec(b.Open), Dec(b.High), Dec(b.Low), Dec(b.Close), b.Volume);
        })];
    }

    private static decimal Dec(double value) => Math.Round((decimal)value, 8);

    private static List<Bar15> Build15(List<Minute> minutes)
    {
        var result = new List<Bar15>();
        var i = 0;
        while (i < minutes.Count)
        {
            var first = minutes[i];
            var number = (first.Clock - OpenMinute) / 15;
            var j = i;
            decimal high = first.High, low = first.Low;
            decimal volume = 0;
            while (j < minutes.Count && minutes[j].Day == first.Day && (minutes[j].Clock - OpenMinute) / 15 == number)
            {
                high = Math.Max(high, minutes[j].High);
                low = Math.Min(low, minutes[j].Low);
                volume += minutes[j].Volume;
                j++;
            }

            var last = minutes[j - 1];
            result.Add(new Bar15
            {
                Day = first.Day,
                Number = number + 1,
                FirstClock = OpenMinute + number * 15,
                LastIndex = j - 1,
                EndEpoch = first.Epoch - (first.Clock - (OpenMinute + number * 15)) * 60L + 15 * 60L,
                Open = first.Open,
                High = high,
                Low = low,
                Close = last.Close,
                Volume = volume,
            });
            i = j;
        }

        return result;
    }

    private static void FillVwapAndAtr(List<Bar15> bars)
    {
        decimal turnover = 0, volume = 0, atr = 0;
        var day = -1;
        var ranges = new List<decimal>();
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (b.Day != day)
            {
                day = b.Day;
                turnover = 0;
                volume = 0;
            }

            turnover += (b.High + b.Low + b.Close) / 3 * b.Volume;
            volume += b.Volume;
            b.Vwap = turnover / volume;

            var range = i == 0 ? b.High - b.Low : Math.Max(b.High - b.Low, Math.Max(Math.Abs(b.High - bars[i - 1].Close), Math.Abs(b.Low - bars[i - 1].Close)));
            ranges.Add(range);
            if (ranges.Count == 14)
            {
                atr = ranges.Average();
                b.Atr = atr;
            }
            else if (ranges.Count > 14)
            {
                atr = (atr * 13 + range) / 14;
                b.Atr = atr;
            }
        }
    }

    private sealed record Coarse(int Day, long EndEpoch, decimal Close);

    private static List<Coarse> BuildHourly(List<Minute> minutes, bool dropStub)
    {
        var result = new List<Coarse>();
        var i = 0;
        while (i < minutes.Count)
        {
            var first = minutes[i];
            var bucket = (first.Clock - OpenMinute) / 60;
            var j = i;
            while (j < minutes.Count && minutes[j].Day == first.Day && (minutes[j].Clock - OpenMinute) / 60 == bucket)
            {
                j++;
            }

            if (!(dropStub && bucket == 6))
            {
                var endClock = bucket == 6 ? 15 * 60 + 30 : OpenMinute + 60 * (bucket + 1);
                result.Add(new Coarse(first.Day, first.Epoch - (first.Clock - endClock) * 60L, minutes[j - 1].Close));
            }

            i = j;
        }

        return result;
    }

    private static List<Coarse> BuildDaily(List<Minute> minutes) =>
        [.. minutes.GroupBy(m => m.Day).OrderBy(g => g.Key).Select(g => new Coarse(g.Key, 0, g.Last().Close))];

    private static decimal?[] Ema(List<decimal> closes, int period)
    {
        var result = new decimal?[closes.Count];
        var factor = 2m / (period + 1);
        decimal previous = 0;
        for (var i = 0; i < closes.Count; i++)
        {
            if (i == period - 1)
            {
                previous = closes.Take(period).Average();
                result[i] = previous;
            }
            else if (i >= period)
            {
                previous += factor * (closes[i] - previous);
                result[i] = previous;
            }
        }

        return result;
    }
}

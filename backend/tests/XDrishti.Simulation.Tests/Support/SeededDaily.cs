using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// One invented daily series of P3-03e: its bars (3 to 12 consecutive sessions of 1-minute bars), one hand-made candidate per session whose 15:29 minute is in the list (decided at 15:30, trigger close the close of that minute, stop 0.5 % to 4 %
/// under it), the calendar the generator used (the truth), the one the walker is given (the truth in 70 % of the series, none in 30 %) and the cases that were planted.
/// </summary>
internal sealed record DailySeries(
    int Seed,
    decimal Tick,
    int Sessions,
    double RewardRisk,
    TradingCalendar Truth,
    TradingCalendar? Given,
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<Bar> Bars,
    IReadOnlyList<EntryCandidate> Candidates,
    IReadOnlySet<string> Tags)
{
    public WalkRules Rules(FillRules fills) =>
        new(RewardRisk, WalkRules.NoSquareOff, fills, TickSpec.FromRupees(Tick).Value, SwingHold.Create(Sessions, Given).Value);

    /// <summary>The series cut after the last minute of the session numbered <paramref name="sessions"/> (1 to the number of sessions): the data of an earlier <c>To</c>.</summary>
    public IReadOnlyList<Bar> Prefix(int sessions)
    {
        var last = Dates[sessions - 1];
        return [.. Bars.Where(b => SessionClock.DateOf(b.EpochSeconds) <= last)];
    }

    /// <summary>The date of the decision of a candidate (the India date of its own minute).</summary>
    public static DateOnly DecisionDate(EntryCandidate candidate) => SessionClock.DateOf(candidate.DecisionEpochSeconds - 60);
}

/// <summary>
/// The generator of the daily properties (spec P3-03e, <i>Test plan</i>). Seed = the series index, SplitMix64 (<see cref="SeededSeries"/>), no <c>System.Random</c>. Three to twelve consecutive sessions from a calendar with one planted holiday; a
/// random walk of up to 0.15 % per minute (a calm half of the series moves a tenth of that); a price log-uniform from 20 to 5,000; a tick of 0.01, 0.05 or 0.10; an overnight gap of 0.2 % to 8 % on half of the nights; 15 % of the sessions lose their first
/// 1 to 6 minutes; 5 % flat minutes; 1 % minutes with volume 0; a hold of 1 to 5 sessions. Planted: a gap above the limit (+0.3 % to +5 %) at one night in 20 % of the series, a gap through the stop (−4 % to −8 %) in 10 %.
/// </summary>
internal static class SeededDaily
{
    public const int Count = 500;

    private static readonly decimal[] Ticks = [0.01m, 0.05m, 0.10m];

    private static readonly double[] Multiples = [1.5, 2.0, 2.5, 3.0];

    private static readonly Lazy<IReadOnlyList<DailySeries>> AllSeries = new(() => [.. Enumerable.Range(1, Count).Select(Make)]);

    /// <summary>The 500 series, seeds 1 to 500, made once.</summary>
    public static IReadOnlyList<DailySeries> All => AllSeries.Value;

    private static double Snap(double price, decimal grid) => (double)Math.Max(grid, Math.Round((decimal)price / grid) * grid);

    public static DailySeries Make(int seed)
    {
        var rng = new SeededSeries(((ulong)seed * 7_919UL) + 17UL);
        var tick = Ticks[rng.Int(Ticks.Length)];
        var hold = 1 + rng.Int(5);
        var rewardRisk = Multiples[rng.Int(Multiples.Length)];
        var count = 3 + rng.Int(10);
        var calm = rng.Unit() < 0.5;
        var start = TestCalendars.Mon.AddDays(7 * rng.Int(4));

        var weekdays = new List<DateOnly>();
        for (var date = start; weekdays.Count < count + 2; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays.Add(date);
            }
        }

        var holidays = new List<DateOnly>();
        if (rng.Unit() < 0.5)
        {
            holidays.Add(weekdays[1 + rng.Int(count - 1)]);
        }

        var truth = TestCalendars.Of(holidays, []);
        var dates = new List<DateOnly>();
        for (var date = start; dates.Count < count; date = date.AddDays(1))
        {
            if (truth.IsTradingDay(date))
            {
                dates.Add(date);
            }
        }

        var tags = new HashSet<string>();
        var gapUpNight = rng.Unit() < 0.20 ? 1 + rng.Int(count - 1) : -1;
        var gapDownNight = rng.Unit() < 0.10 ? 1 + rng.Int(count - 1) : -1;
        var bars = Sessions(rng, dates, tick, calm, gapUpNight, gapDownNight, tags);
        if (gapUpNight >= 0)
        {
            tags.Add("gap-above-limit");
        }

        if (gapDownNight >= 0)
        {
            tags.Add("gap-through-stop");
        }

        var given = rng.Unit() < 0.7 ? truth : null;
        var candidates = Candidates(rng, bars, dates, tick);
        return new DailySeries(seed, tick, hold, rewardRisk, truth, given, dates, bars, candidates, tags);
    }

    private static List<Bar> Sessions(SeededSeries rng, List<DateOnly> dates, decimal tick, bool calm, int gapUpNight, int gapDownNight, HashSet<string> tags)
    {
        var price = Math.Exp(Math.Log(20) + (rng.Unit() * (Math.Log(5_000) - Math.Log(20))));
        var scale = calm ? 0.12 : 1.0;
        var bars = new List<Bar>();
        for (var d = 0; d < dates.Count; d++)
        {
            if (d > 0)
            {
                if (d == gapUpNight)
                {
                    price *= 1 + 0.003 + (rng.Unit() * 0.047);
                }
                else if (d == gapDownNight)
                {
                    price *= 1 - (0.04 + (rng.Unit() * 0.04));
                }
                else if (rng.Unit() < 0.5)
                {
                    price *= 1 + ((rng.Unit() < 0.5 ? -1 : 1) * (0.002 + (rng.Unit() * 0.078)));
                }
            }

            var skip = rng.Unit() < 0.15 ? 1 + rng.Int(6) : 0;
            if (skip > 0)
            {
                tags.Add("late-first-minute");
            }

            for (var minute = SessionClock.OpenMinute; minute < SessionClock.CloseMinute; minute++)
            {
                var open = Snap(price, tick);
                double high, low, close;
                if (rng.Unit() < 0.05)
                {
                    high = low = close = open;
                }
                else
                {
                    close = open * (1 + ((rng.Unit() - 0.5) * 0.003 * scale));
                    high = Math.Max(open, close) * (1 + (rng.Unit() * 0.0006 * scale));
                    low = Math.Min(open, close) * (1 - (rng.Unit() * 0.0006 * scale));
                }

                close = Snap(close, tick);
                high = Math.Max(Snap(high, tick), Math.Max(open, close));
                low = Math.Min(Snap(low, tick), Math.Min(open, close));
                var volume = Day.MinuteVolume;
                if (rng.Unit() < 0.01)
                {
                    volume = 0;
                    high = low = close = open;
                }

                price = close;
                if (minute - SessionClock.OpenMinute >= skip)
                {
                    bars.Add(new Bar(Day.Epoch(dates[d], minute), open, high, low, close, volume));
                }
            }
        }

        return bars;
    }

    /// <summary>One candidate per session whose 15:29 minute is in the list: decided at 15:30, trigger close the close of that minute, a stop 0.5 % to 4 % under it.</summary>
    private static List<EntryCandidate> Candidates(SeededSeries rng, List<Bar> bars, List<DateOnly> dates, decimal tick)
    {
        var candidates = new List<EntryCandidate>();
        foreach (var date in dates)
        {
            var epoch = Day.Epoch(date, SessionClock.CloseMinute - 1);
            var index = bars.FindIndex(b => b.EpochSeconds == epoch);
            var fraction = 0.005 + (rng.Unit() * 0.035);
            if (index < 0)
            {
                continue;
            }

            var own = bars[index];
            var stop = Math.Max((double)(Math.Round((decimal)(own.Close * (1 - fraction)) / tick) * tick), (double)tick);
            var daily = new Bar(Day.Epoch(date, SessionClock.OpenMinute), own.Open, own.High, own.Low, own.Close, 375_000);
            candidates.Add(new EntryCandidate("SWING_DAILY_TEST", Cand.Test, Side.Buy, index, own.EpochSeconds + 60, Timeframe.D1, daily, 375, TriggerBarComplete: true, stop, DecisionTrace.Empty));
        }

        return candidates;
    }
}

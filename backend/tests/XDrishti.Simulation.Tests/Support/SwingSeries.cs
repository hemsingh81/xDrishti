using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// One invented multi-day instrument of the swing properties (P3-03d, <i>The seeded generator</i>): its bars, the hand-built candidates (the position gate takes candidates, not strategies), the calendar the generator used (the truth),
/// the calendar the walker is given (the truth in 70 % of the series, none in 30 %) and the cases that were planted.
/// </summary>
internal sealed record SwingSeries(
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
    public WalkRules Rules(FillRules fills, TradingCalendar? calendar) =>
        new(RewardRisk, WalkRules.NoSquareOff, fills, TickSpec.FromRupees(Tick).Value, SwingHold.Create(Sessions, calendar).Value);

    /// <summary>The rules of the walker as the series is meant to be walked: the calendar it is given (or none).</summary>
    public WalkRules Rules(FillRules fills) => Rules(fills, Given);

    /// <summary>The due session of an entry date by the test's own scan of the calendar the walker is given.</summary>
    public DateOnly Due(DateOnly entryDate) => SwingOracle.DueDate(entryDate, Sessions, Given);

    public int DueMinute(DateOnly due) => SwingOracle.DueMinute(due, Given);
}

/// <summary>The test's own reading of the calendar and of the hold, written from the same sentences as <c>SwingHold</c> but with its own scan (criterion 22).</summary>
internal static class SwingOracle
{
    public static bool IsSession(DateOnly date, TradingCalendar? calendar) =>
        calendar is null ? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) : calendar.SessionOf(date) is not null;

    public static DateOnly DueDate(DateOnly entryDate, int sessions, TradingCalendar? calendar)
    {
        var date = entryDate;
        var left = sessions - 1;
        while (left > 0)
        {
            date = date.AddDays(1);
            if (IsSession(date, calendar))
            {
                left--;
            }
        }

        return date;
    }

    /// <summary>The minute that starts one minute before the end of the session (15:29 when there is no session known).</summary>
    public static int DueMinute(DateOnly due, TradingCalendar? calendar) => (calendar?.SessionOf(due)?.ToMinute ?? 930) - 1;

    /// <summary>The sessions from the entry date to the date of the exit, both included, by the test's own count.</summary>
    public static int SessionsHeld(DateOnly entryDate, DateOnly exitDate, TradingCalendar? calendar)
    {
        var held = 1;
        for (var date = entryDate.AddDays(1); date <= exitDate; date = date.AddDays(1))
        {
            if (IsSession(date, calendar))
            {
                held++;
            }
        }

        return held;
    }
}

/// <summary>
/// The generator. Seed = the series index, SplitMix64 (<see cref="SeededSeries"/>), no <c>System.Random</c>. Three to eight consecutive sessions from a calendar with one planted holiday and, in a third of the series, a planted
/// special session (Saturday, 09:15 to 12:30); a random walk of up to 0.15 % per minute (a calm half of the series moves a tenth of that, so that stops and targets are not always reached); a price log-uniform from 20 to 5,000; a tick of
/// 0.01, 0.05 or 0.10; overnight gaps of 0.2 % to 8 % on a quarter of the nights; 0 to 15 % of the minutes removed in runs of 1 to 6; 5 % flat minutes; 1 % minutes with volume 0; a hold of 1 to 5 sessions; one to three candidates between
/// 10:15 and 14:15, a few of them inside the hold of the first. The cases a random walk makes too rarely are planted (see <see cref="Plant"/>).
/// </summary>
internal static class SeededSwing
{
    public const int Count = 500;

    private static readonly decimal[] Ticks = [0.01m, 0.05m, 0.10m];

    private static readonly double[] Multiples = [1.5, 2.0, 2.5, 3.0];

    private static readonly Lazy<IReadOnlyList<SwingSeries>> AllSeries = new(() => [.. Enumerable.Range(1, Count).Select(Make)]);

    /// <summary>The 500 series, seeds 1 to 500, made once.</summary>
    public static IReadOnlyList<SwingSeries> All => AllSeries.Value;

    private static double Snap(double price, decimal grid) => (double)Math.Max(grid, Math.Round((decimal)price / grid) * grid);

    public static SwingSeries Make(int seed)
    {
        var rng = new SeededSeries(((ulong)seed * 104_729UL) + 31UL);
        var tick = Ticks[rng.Int(Ticks.Length)];
        var sessions = 1 + rng.Int(5);
        var rewardRisk = Multiples[rng.Int(Multiples.Length)];
        var count = 3 + rng.Int(6);
        var calm = rng.Unit() < 0.5;
        var start = TestCalendars.Mon.AddDays(7 * rng.Int(4));

        // The truth: one holiday among the weekdays after the first, a special Saturday in a third of the series.
        var weekdays = new List<DateOnly>();
        for (var date = start; weekdays.Count < count + 2; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays.Add(date);
            }
        }

        var holidays = new List<DateOnly> { weekdays[1 + rng.Int(count - 1)] };
        var specials = new List<(DateOnly, int, int)>();
        if (rng.Unit() < 1.0 / 3.0)
        {
            specials.Add((start.AddDays(5), (9 * 60) + 15, (12 * 60) + 30));
        }

        var truth = TestCalendars.Of(holidays, specials);
        var dates = new List<DateOnly>();
        for (var date = start; dates.Count < count; date = date.AddDays(1))
        {
            if (truth.IsTradingDay(date))
            {
                dates.Add(date);
            }
        }

        var given = rng.Unit() < 0.7 ? truth : null;
        var bars = Sessions(rng, truth, dates, tick, calm);
        Thin(rng, bars);

        var tags = new HashSet<string>();
        var plans = PlanCandidates(rng, bars, dates, sessions, given, truth);
        Plant(rng, bars, plans, dates, truth, given, sessions, rewardRisk, tick, tags);

        var candidates = plans.Select(p => Build(bars, p, tick)).OfType<EntryCandidate>().OrderBy(c => c.DecisionEpochSeconds).ToList();
        return new SwingSeries(seed, tick, sessions, rewardRisk, truth, given, dates, bars, candidates, tags);
    }

    /// <summary>The bars of every session of the truth, minute by minute.</summary>
    private static List<Bar> Sessions(SeededSeries rng, TradingCalendar truth, List<DateOnly> dates, decimal tick, bool calm)
    {
        var price = Math.Exp(Math.Log(20) + (rng.Unit() * (Math.Log(5_000) - Math.Log(20))));
        var scale = calm ? 0.12 : 1.0;
        var bars = new List<Bar>();
        for (var d = 0; d < dates.Count; d++)
        {
            var session = truth.SessionOf(dates[d])!.Value;
            if (d > 0 && rng.Unit() < 0.25)
            {
                price *= 1 + ((rng.Unit() < 0.5 ? -1 : 1) * (0.002 + (rng.Unit() * 0.078)));
            }

            for (var minute = session.FromMinute; minute < session.ToMinute; minute++)
            {
                var open = Snap(price, tick);
                double high, low, close;
                var roll = rng.Unit();
                if (roll < 0.05)
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
                    if (rng.Unit() < 0.6)
                    {
                        high = low = close = open;
                    }
                }

                bars.Add(new Bar(Day.Epoch(dates[d], minute), open, high, low, close, volume));
                price = close;
            }
        }

        return bars;
    }

    /// <summary>Removes 0 to 15 % of the minutes in runs of 1 to 6.</summary>
    private static void Thin(SeededSeries rng, List<Bar> bars)
    {
        var removed = new HashSet<int>();
        var target = (int)(rng.Unit() * 0.15 * bars.Count);
        while (removed.Count < target)
        {
            var start = rng.Int(bars.Count);
            for (var j = start; j < Math.Min(bars.Count, start + 1 + rng.Int(6)); j++)
            {
                removed.Add(j);
            }
        }

        var kept = bars.Where((_, i) => !removed.Contains(i)).ToList();
        bars.Clear();
        bars.AddRange(kept);
    }

    /// <summary>A candidate before the cases are planted: the minute that closes its bar (by epoch, so that removing minutes cannot move it) and how far its stop is.</summary>
    private sealed class Plan(long ownEpoch, double stopFraction, bool snapStop)
    {
        public long OwnEpoch { get; } = ownEpoch;

        public double StopFraction { get; } = stopFraction;

        public bool SnapStop { get; } = snapStop;
    }

    private static List<Plan> PlanCandidates(SeededSeries rng, List<Bar> bars, List<DateOnly> dates, int sessions, TradingCalendar? given, TradingCalendar truth)
    {
        var plans = new List<Plan>();
        var count = dates.Count;
        var entry = rng.Unit() < 0.08 ? count - 1 : rng.Int(Math.Max(1, count - 1));
        var first = PickMinute(rng, bars, dates[entry], truth, 0);
        plans.Add(new Plan(first, 0.002 + (rng.Unit() * 0.028), rng.Unit() < 0.5));
        for (var extra = rng.Int(3); extra > 0; extra--)
        {
            int day;
            if (rng.Unit() < 0.6)
            {
                var last = Math.Min(count - 1, entry + sessions - 1);
                day = entry + rng.Int(last - entry + 1);
            }
            else
            {
                day = Math.Min(count - 1, entry + 1 + rng.Int(Math.Max(1, count - entry - 1)));
            }

            // On the entry day a second candidate is at least half an hour after the first, so that it is decided inside the position.
            var after = day == entry ? first + (30 * 60) : 0;
            plans.Add(new Plan(PickMinute(rng, bars, dates[day], truth, after), 0.002 + (rng.Unit() * 0.028), rng.Unit() < 0.5));
        }

        return [.. plans.DistinctBy(p => p.OwnEpoch)];
    }

    /// <summary>The epoch of a minute of the date between 10:15 and 14:15 (inside the session) that exists in the list, later than <paramref name="notBefore"/>.</summary>
    private static long PickMinute(SeededSeries rng, List<Bar> bars, DateOnly date, TradingCalendar truth, long notBefore)
    {
        var end = Math.Min((14 * 60) + 15, (truth.SessionOf(date)?.ToMinute ?? 930) - 15);
        var eligible = bars.Where(b => SessionClock.DateOf(b.EpochSeconds) == date && SessionClock.MinuteOfDay(b.EpochSeconds) is >= (10 * 60) + 15 && SessionClock.MinuteOfDay(b.EpochSeconds) <= end && b.EpochSeconds > notBefore).ToList();
        if (eligible.Count == 0)
        {
            eligible = [.. bars.Where(b => SessionClock.DateOf(b.EpochSeconds) == date)];
        }

        return eligible.Count == 0 ? bars[rng.Int(bars.Count)].EpochSeconds : eligible[rng.Int(eligible.Count)].EpochSeconds;
    }

    private static EntryCandidate? Build(List<Bar> bars, Plan plan, decimal tick)
    {
        var index = bars.FindIndex(b => b.EpochSeconds == plan.OwnEpoch);
        if (index < 0)
        {
            return null;
        }

        var own = bars[index];
        var close = own.Close;
        var stop = close * (1 - plan.StopFraction);
        if (plan.SnapStop)
        {
            stop = (double)(Math.Round((decimal)stop / tick) * tick);
        }

        stop = Math.Max(stop, (double)tick);
        var trigger = new Bar(own.EpochSeconds - (59 * 60), own.Open, own.High, own.Low, close, 60_000);
        return new EntryCandidate("SWING_TEST", Cand.Test, Side.Buy, index, own.EpochSeconds + 60, Timeframe.H1, trigger, 60, TriggerBarComplete: true, stop, DecisionTrace.Empty);
    }

    /// <summary>
    /// The planted cases (the starting rates of the spec, tuned in the build): 10 % of the series end inside the hold; 8 % lose the due minute (absent or volume 0); 6 % lose a whole session inside the hold; 12 % open a night 0.5 to 3 stop
    /// distances beyond the stop; 8 % open a night beyond the target; 15 % (5 % in the spec) hold a minute that reaches both levels on the first later day; 20 % (8 % in the spec) have a due minute that is missing with a later session present, and 60 % (a third in the spec) of those a hand-built
    /// candidate at 15:15 and at 15:30 of the due session after its last held minute (the case G2b of D11) and one the day after.
    /// </summary>
    private static void Plant(SeededSeries rng, List<Bar> bars, List<Plan> plans, List<DateOnly> dates, TradingCalendar truth, TradingCalendar? given, int sessions, double rewardRisk, decimal tick, HashSet<string> tags)
    {
        var first = bars.FindIndex(b => b.EpochSeconds == plans[0].OwnEpoch);
        if (first < 0)
        {
            return;
        }

        var protectedEpochs = new HashSet<long>();
        foreach (var plan in plans)
        {
            for (var k = -1; k <= 6; k++)
            {
                protectedEpochs.Add(plan.OwnEpoch + (k * 60L));
            }
        }

        var entryDate = SessionClock.DateOf(plans[0].OwnEpoch);
        var due = SwingOracle.DueDate(entryDate, sessions, given);
        var dueMinute = SwingOracle.DueMinute(due, given);
        var entryPrice = bars[first].Close;
        var stop = entryPrice * (1 - plans[0].StopFraction);
        var risk = Math.Max(entryPrice - stop, (double)tick);
        var target = entryPrice + (rewardRisk * risk);
        var inHold = dates.Where(d => d > entryDate && d <= due).ToList();

        int IndexOf(long epoch) => bars.FindIndex(b => b.EpochSeconds == epoch);
        int FirstOn(DateOnly date) => bars.FindIndex(b => SessionClock.DateOf(b.EpochSeconds) == date);

        void Set(int index, double open, double high, double low, double close)
        {
            if (index < 0 || protectedEpochs.Contains(bars[index].EpochSeconds))
            {
                return;
            }

            var o = Snap(open, tick);
            var c = Snap(close, tick);
            bars[index] = bars[index] with { Open = o, High = Math.Max(Snap(high, tick), Math.Max(o, c)), Low = Math.Min(Snap(low, tick), Math.Min(o, c)), Close = c, Volume = Day.MinuteVolume };
        }

        // 12 %: a night opens 0.5 to 3 stop distances beyond the stop.
        if (inHold.Count > 0 && rng.Unit() < 0.12)
        {
            var open = stop - ((0.5 + (rng.Unit() * 2.5)) * risk);
            Set(FirstOn(inHold[rng.Int(inHold.Count)]), open, open + (risk * 0.1), open - (risk * 0.1), open);
            tags.Add("night-beyond-stop");
        }

        // 8 %: a night opens beyond the target.
        if (inHold.Count > 0 && rng.Unit() < 0.08)
        {
            var open = target + ((0.1 + (rng.Unit() * 1.9)) * risk);
            Set(FirstOn(inHold[rng.Int(inHold.Count)]), open, open + (risk * 0.1), open - (risk * 0.1), open);
            tags.Add("night-beyond-target");
        }

        // 15 % (the spec started at 5 %, too few positions were still open): a minute of the first later day reaches both levels with its open between them.
        if (inHold.Count > 0 && rng.Unit() < 0.15)
        {
            var date = inHold[0];
            var at = FirstOn(date) + 10 + rng.Int(60);
            if (at < bars.Count && SessionClock.DateOf(bars[at].EpochSeconds) == date)
            {
                Set(at, entryPrice, target + (risk * 0.5), stop - (risk * 0.5), entryPrice);
                tags.Add("both-levels");
            }
        }

        // 6 %: a whole session between the entry session and the due session is removed.
        var between = dates.Where(d => d > entryDate && d < due).ToList();
        if (between.Count > 0 && rng.Unit() < 0.06)
        {
            var gone = between[rng.Int(between.Count)];
            bars.RemoveAll(b => SessionClock.DateOf(b.EpochSeconds) == gone && !protectedEpochs.Contains(b.EpochSeconds));
            tags.Add("session-removed");
        }

        // 20 %: the due minute is missing with a later session present; 60 % of those carry hand-built candidates at 15:15 and 15:30 and the day after.
        var dueIndex = dates.IndexOf(due);
        var laterSession = dueIndex >= 0 && dueIndex + 1 < dates.Count;
        var dueEpoch = Day.Epoch(due, dueMinute);
        if (laterSession && rng.Unit() < 0.20)
        {
            var withCandidates = rng.Unit() < 0.6;
            var end = (truth.SessionOf(due)?.ToMinute ?? 930);
            var ownBefore = Day.Epoch(due, end - 16);
            var ownAtEnd = Day.Epoch(due, dueMinute);
            if (withCandidates && IndexOf(ownBefore) >= 0 && IndexOf(dueEpoch) >= 0)
            {
                protectedEpochs.Add(ownBefore);
                protectedEpochs.Add(dueEpoch);
                var index = IndexOf(dueEpoch);
                bars[index] = bars[index] with { Volume = 0 };
                plans.Add(new Plan(ownBefore, 0.002 + (rng.Unit() * 0.028), true));
                plans.Add(new Plan(ownAtEnd, 0.002 + (rng.Unit() * 0.028), true));
                var next = dates[dueIndex + 1];
                var nextMinute = bars.FindIndex(b => SessionClock.DateOf(b.EpochSeconds) == next && SessionClock.MinuteOfDay(b.EpochSeconds) >= (10 * 60) + 15);
                if (nextMinute >= 0)
                {
                    plans.Add(new Plan(bars[nextMinute].EpochSeconds, 0.002 + (rng.Unit() * 0.028), true));
                }

                tags.Add("due-minute-empty-with-candidates");
            }
            else if (!protectedEpochs.Contains(dueEpoch))
            {
                if (rng.Unit() < 0.8)
                {
                    bars.RemoveAll(b => b.EpochSeconds == dueEpoch);
                }
                else
                {
                    var index = IndexOf(dueEpoch);
                    if (index >= 0)
                    {
                        bars[index] = bars[index] with { Volume = 0 };
                    }
                }

                tags.Add("due-minute-missing");
            }
        }
        else if (rng.Unit() < 0.08 && !protectedEpochs.Contains(dueEpoch))
        {
            // 8 %: the due minute is lost whether or not a session follows.
            bars.RemoveAll(b => b.EpochSeconds == dueEpoch);
            tags.Add("due-minute-missing");
        }

        // 10 %: the list ends inside the hold.
        if (rng.Unit() < 0.10)
        {
            var lastDay = dates.LastOrDefault(d => d <= due && d >= entryDate);
            var window = bars.Where(b => b.EpochSeconds > plans[0].OwnEpoch + (600 * 60L) && SessionClock.DateOf(b.EpochSeconds) <= lastDay).ToList();
            if (window.Count > 0)
            {
                var cut = window[rng.Int(window.Count)].EpochSeconds;
                bars.RemoveAll(b => b.EpochSeconds > cut);
                tags.Add("list-ends-in-hold");
            }
        }
    }
}

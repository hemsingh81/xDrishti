using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A candidate and the rules it is walked with (the fill rules are those of the property, put in by the test).</summary>
internal sealed record SeededWalk(EntryCandidate Candidate, WalkRules Rules);

/// <summary>One invented session with its tick, its square-off, its minutes and the candidates on it.</summary>
internal sealed record SeededDay(int Seed, decimal Tick, int SquareOffMinute, IReadOnlyList<Bar> Bars, IReadOnlyList<SeededWalk> Walks);

/// <summary>
/// The seeded days of the properties (P3-03b, <i>The seeded generator</i>): seed = the day's index, a deterministic generator without <c>System.Random</c> (SplitMix64, the same numbers on every machine). A regular session of 375
/// minutes from a random walk with a move of up to 0.15 % per minute; a price log-uniform from 20 to 5,000; a tick of 0.01, 0.05 or 0.10 and, on about 15 % of the days, a grid of 0.01 whatever the tick; 0 to 15 % of the minutes
/// removed in runs of 1 to 6; 0 to 3 gaps of 0.3 % to 5 % between consecutive minutes; 5 % flat minutes, 2 % spike minutes (a range of 1 % to 6 %), 1 % minutes with volume 0; one to three candidates per side between 09:30 and 14:30,
/// one in ten within three minutes before the square-off; the stop 0.2 % to 3 % from the trigger close; a reward-to-risk of 1.5, 2, 2.5 or 3; a square-off of 14:30, 15:15 or 15:29. The cases that a random walk makes too rarely are
/// planted: a gap rewritten into the fill minute (8 % of the candidates), an ambiguous minute some time after the decision (6 %), a day that ends before the square-off (10 %), one that loses the minutes at the square-off (12 %) and a
/// candidate at the last minute of the day (4 %). The thresholds of the properties are met by the planted cases and not by the random walk alone.
/// </summary>
internal static class SeededDays
{
    public const int Count = 500;

    private static readonly decimal[] Ticks = [0.01m, 0.05m, 0.10m];

    private static readonly double[] Multiples = [1.5, 2.0, 2.5, 3.0];

    private static readonly int[] SquareOffs = [(14 * 60) + 30, (15 * 60) + 15, (15 * 60) + 29];

    private static readonly Lazy<IReadOnlyList<SeededDay>> AllDays = new(() => [.. Enumerable.Range(1, Count).Select(Make)]);

    /// <summary>The 500 days, seeds 1 to 500, made once.</summary>
    public static IReadOnlyList<SeededDay> All => AllDays.Value;

    public static SeededDay Make(int seed)
    {
        var rng = new SeededSeries(((ulong)seed * 7_919UL) + 17UL);
        var tick = Ticks[rng.Int(Ticks.Length)];
        var grid = rng.Unit() < 0.15 ? 0.01m : tick;
        var squareOff = SquareOffs[rng.Int(SquareOffs.Length)];
        var dense = Session(rng, grid);
        var bars = Thin(rng, dense, squareOff);

        var plans = PlanCandidates(rng, bars, squareOff, grid);
        PlantFillGaps(rng, bars, plans, grid);
        PlantAmbiguousMinutes(rng, bars, plans, squareOff, grid);

        var walks = plans.Select(p => new SeededWalk(p.Candidate, new WalkRules(p.RewardRisk, squareOff, FillRules.Default, TickSpec.FromRupees(tick).Value))).ToList();
        return new SeededDay(seed, tick, squareOff, bars, walks);
    }

    private static double Snap(double price, decimal grid) => (double)Math.Max(grid, Math.Round((decimal)price / grid) * grid);

    /// <summary>A dense session of 375 minutes with flat, spike and empty minutes and 0 to 3 gaps.</summary>
    private static List<Bar> Session(SeededSeries rng, decimal grid)
    {
        var date = DayBuilder.Monday;
        var price = Math.Exp(Math.Log(20) + (rng.Unit() * (Math.Log(5_000) - Math.Log(20))));
        var gapAt = new Dictionary<int, double>();
        for (var g = rng.Int(4); g > 0; g--)
        {
            gapAt[1 + rng.Int(SessionClock.RegularBars - 1)] = (rng.Unit() < 0.5 ? -1 : 1) * (0.003 + (rng.Unit() * 0.047));
        }

        var bars = new List<Bar>(SessionClock.RegularBars);
        for (var i = 0; i < SessionClock.RegularBars; i++)
        {
            if (gapAt.TryGetValue(i, out var jump))
            {
                price *= 1 + jump;
            }

            var open = Snap(price, grid);
            double high, low, close;
            var roll = rng.Unit();
            if (roll < 0.05)
            {
                high = low = close = open;
            }
            else if (roll < 0.07)
            {
                var range = 0.01 + (rng.Unit() * 0.05);
                high = open * (1 + (range / 2));
                low = open * (1 - (range / 2));
                close = open * (1 + ((rng.Unit() - 0.5) * range));
            }
            else
            {
                close = open * (1 + ((rng.Unit() - 0.5) * 0.003));
                high = Math.Max(open, close) * (1 + (rng.Unit() * 0.0008));
                low = Math.Min(open, close) * (1 - (rng.Unit() * 0.0008));
            }

            close = Snap(close, grid);
            high = Math.Max(Snap(high, grid), Math.Max(open, close));
            low = Math.Min(Snap(low, grid), Math.Min(open, close));
            var volume = Day.MinuteVolume;
            if (rng.Unit() < 0.01)
            {
                volume = 0;
                if (rng.Unit() < 0.6)
                {
                    high = low = close = open;
                }
            }

            bars.Add(new Bar(DayBuilder.Epoch(date, SessionClock.OpenMinute + i), open, high, low, close, volume));
            price = close;
        }

        return bars;
    }

    /// <summary>Removes runs of minutes (0 to 15 % of the day), ends the day early (10 %) and loses the minutes at the square-off (12 %).</summary>
    private static List<Bar> Thin(SeededSeries rng, List<Bar> dense, int squareOff)
    {
        var removed = new HashSet<int>();
        var target = (int)(rng.Unit() * 0.15 * dense.Count);
        while (removed.Count < target)
        {
            var start = rng.Int(dense.Count);
            for (var j = start; j < Math.Min(dense.Count, start + 1 + rng.Int(6)); j++)
            {
                removed.Add(j);
            }
        }

        var endsAt = rng.Unit() < 0.10 ? (rng.Unit() < 0.5 ? (14 * 60) + 10 : (14 * 60) + 50) : int.MaxValue;
        var lostTo = rng.Unit() < 0.12 ? squareOff + 1 + rng.Int(5) : -1;
        var kept = new List<Bar>(dense.Count);
        for (var i = 0; i < dense.Count; i++)
        {
            var minute = SessionClock.MinuteOfDay(dense[i].EpochSeconds);
            if (!removed.Contains(i) && minute <= endsAt && !(minute >= squareOff && minute <= lostTo))
            {
                kept.Add(dense[i]);
            }
        }

        return kept;
    }

    private sealed record Plan(EntryCandidate Candidate, int Index, double RewardRisk, double Risk);

    /// <summary>One to three candidates per side at the decision instants of existing minutes; 10 % within three minutes before the square-off; 4 % of the days get one at the last minute.</summary>
    private static List<Plan> PlanCandidates(SeededSeries rng, List<Bar> bars, int squareOff, decimal grid)
    {
        var plans = new List<Plan>();
        var eligible = Enumerable.Range(0, bars.Count).Where(i => SessionClock.MinuteOfDay(bars[i].EpochSeconds) is >= (9 * 60) + 29 and <= (14 * 60) + 29).ToList();
        if (bars.Count == 0)
        {
            return plans;
        }

        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            for (var c = 1 + rng.Int(3); c > 0; c--)
            {
                var index = rng.Unit() < 0.10 ? NearSquareOff(rng, bars, squareOff) : (eligible.Count > 0 ? eligible[rng.Int(eligible.Count)] : rng.Int(bars.Count));
                plans.Add(MakePlan(rng, bars, index, side, grid));
            }
        }

        if (rng.Unit() < 0.04)
        {
            plans.Add(MakePlan(rng, bars, bars.Count - 1, rng.Unit() < 0.5 ? Side.Buy : Side.Sell, grid));
        }

        return plans;
    }

    private static int NearSquareOff(SeededSeries rng, List<Bar> bars, int squareOff)
    {
        var wanted = squareOff - 1 - rng.Int(3);
        var index = bars.FindIndex(b => SessionClock.MinuteOfDay(b.EpochSeconds) == wanted);
        return index >= 0 ? index : rng.Int(bars.Count);
    }

    private static Plan MakePlan(SeededSeries rng, List<Bar> bars, int index, Side side, decimal grid)
    {
        var own = bars[index];
        var close = own.Close;
        var distance = close * (0.002 + (rng.Unit() * 0.028));
        var stop = side == Side.Buy ? close - distance : close + distance;
        if (rng.Unit() < 0.5)
        {
            stop = Snap(stop, grid);
        }

        stop = Math.Max(stop, (double)grid);
        var trigger = new Bar(own.EpochSeconds - (14 * 60), own.Open, own.High, own.Low, close, 15_000);
        var candidate = new EntryCandidate("ORB", Cand.Test, side, index, own.EpochSeconds + 60, Timeframe.M15, trigger, 15, TriggerBarComplete: true, stop, DecisionTrace.Empty);
        return new Plan(candidate, index, Multiples[rng.Int(Multiples.Length)], Math.Abs(close - stop));
    }

    /// <summary>8 % of the candidates: the first minute after the candidate's own is rewritten as a gap of 0.05 to 3 stop distances, up or down.</summary>
    private static void PlantFillGaps(SeededSeries rng, List<Bar> bars, List<Plan> plans, decimal grid)
    {
        var owns = plans.Select(p => p.Index).ToHashSet();
        foreach (var plan in plans)
        {
            if (rng.Unit() >= 0.08 || plan.Index + 1 >= bars.Count || owns.Contains(plan.Index + 1))
            {
                continue;
            }

            var gap = (0.05 + (rng.Unit() * 2.95)) * plan.Risk * (rng.Unit() < 0.5 ? -1 : 1);
            var open = Snap(plan.Candidate.TriggerBar.Close + gap, grid);
            var wick = plan.Risk * 0.3;
            var high = Snap(open + (rng.Unit() * wick), grid);
            var low = Snap(open - (rng.Unit() * wick), grid);
            var close = Snap(low + (rng.Unit() * (high - low)), grid);
            bars[plan.Index + 1] = bars[plan.Index + 1] with { Open = open, High = Math.Max(high, Math.Max(open, close)), Low = Math.Min(low, Math.Min(open, close)), Close = close, Volume = Day.MinuteVolume };
        }
    }

    /// <summary>6 % of the candidates: a minute 5 to 60 minutes after the decision opens at the trigger close and reaches half a risk beyond the target and half a risk beyond the stop.</summary>
    private static void PlantAmbiguousMinutes(SeededSeries rng, List<Bar> bars, List<Plan> plans, int squareOff, decimal grid)
    {
        var owns = plans.Select(p => p.Index).ToHashSet();
        foreach (var plan in plans)
        {
            if (rng.Unit() >= 0.06)
            {
                continue;
            }

            var decision = SessionClock.MinuteOfDay(plan.Candidate.DecisionEpochSeconds);
            var from = decision + 5;
            var to = Math.Min(decision + 60, squareOff - 1);
            var choices = Enumerable.Range(plan.Index + 1, Math.Max(0, bars.Count - plan.Index - 1))
                .Where(i => !owns.Contains(i) && SessionClock.MinuteOfDay(bars[i].EpochSeconds) >= from && SessionClock.MinuteOfDay(bars[i].EpochSeconds) <= to)
                .ToList();
            if (choices.Count == 0)
            {
                continue;
            }

            var index = choices[rng.Int(choices.Count)];
            var close = plan.Candidate.TriggerBar.Close;
            var buy = plan.Candidate.Side == Side.Buy;
            var beyondTarget = Snap(buy ? close + ((plan.RewardRisk + 0.5) * plan.Risk) : close - ((plan.RewardRisk + 0.5) * plan.Risk), grid);
            var beyondStop = Snap(buy ? close - (1.5 * plan.Risk) : close + (1.5 * plan.Risk), grid);
            var open = Snap(close, grid);
            bars[index] = bars[index] with
            {
                Open = open,
                High = Math.Max(beyondTarget, beyondStop),
                Low = Math.Max(Math.Min(beyondTarget, beyondStop), (double)grid),
                Close = open,
                Volume = Day.MinuteVolume,
            };
        }
    }
}

using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>One seeded series of one to three sessions of 1-minute bars with 2 to 6 hand-less (random) candidates on it, and the exit rules of each.</summary>
internal sealed record SeededRun(int Seed, IReadOnlyList<Bar> Bars, IReadOnlyList<SeededWalk> Walks);

/// <summary>A candidate and the rules it is walked with.</summary>
internal sealed record SeededWalk(EntryCandidate Candidate, WalkRules Rules);

/// <summary>
/// The seeded generator of the properties (AC8, AC9). Holes, gaps, spikes, truncated sessions and decisions within three minutes of the square-off are planted so that every exit reason and every no-trade reason occurs;
/// the seeds are written in the tests and the same seed always gives the same series (a plain <see cref="Random"/> with a seed lives in the test project only).
/// </summary>
internal static class SeededRuns
{
    private static readonly double?[] RewardRisks = [1.5, 2.0, 3.0, null];

    private static readonly int[] SquareOffs = [14 * 60 + 30, 15 * 60 + 15];

    public static SeededRun Make(int seed)
    {
        var random = new Random(seed);
        var sessions = 1 + random.Next(3);
        var firstDate = DayBuilder.Monday.AddDays(random.Next(3));
        var dense = new List<Bar>();
        var price = 50 + (random.NextDouble() * 1950);
        for (var s = 0; s < sessions; s++)
        {
            var date = firstDate.AddDays(s);
            price = AddSession(dense, random, date, price);
        }

        var densePositions = PickCandidates(random, dense);
        var walks = new List<(int DenseIndex, EntryCandidate Candidate, WalkRules Rules)>();
        var removed = new HashSet<int>();
        foreach (var position in densePositions)
        {
            walks.Add((position, MakeCandidate(random, dense, position), MakeRules(random)));
        }

        PlantGapsAfterDecisions(random, dense, walks);
        var protectedIndexes = walks.Select(w => w.DenseIndex).ToHashSet();
        PlantHoles(random, dense, protectedIndexes, removed, walks.Select(w => w.DenseIndex).ToList());

        var bars = new List<Bar>();
        var listIndex = new Dictionary<int, int>();
        for (var i = 0; i < dense.Count; i++)
        {
            if (!removed.Contains(i))
            {
                listIndex[i] = bars.Count;
                bars.Add(dense[i]);
            }
        }

        var result = walks.Select(w => new SeededWalk(w.Candidate with { DecisionBarIndex = listIndex[w.DenseIndex] }, w.Rules)).ToList();
        return new SeededRun(seed, bars, result);
    }

    private static double Gaussian(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    /// <summary>One dense session (09:15 to 15:29, or cut short); returns the last close.</summary>
    private static double AddSession(List<Bar> dense, Random random, DateOnly date, double previousClose)
    {
        var last = SessionClock.CloseMinute - 1;
        if (random.NextDouble() < 0.25)
        {
            last = (11 * 60) + random.Next(0, (15 * 60 + 20) - (11 * 60));
        }

        var close = previousClose * (1 + (Gaussian(random) * 0.005));
        for (var minute = SessionClock.OpenMinute; minute <= last; minute++)
        {
            var open = close;
            if (random.NextDouble() < 0.004)
            {
                open *= 1 + ((random.NextDouble() < 0.5 ? -1 : 1) * (0.01 + (random.NextDouble() * 0.03)));
            }

            close = open * (1 + (Gaussian(random) * 0.0007));
            var high = Math.Max(open, close) * (1 + (Math.Abs(Gaussian(random)) * 0.0004));
            var low = Math.Min(open, close) * (1 - (Math.Abs(Gaussian(random)) * 0.0004));
            if (random.NextDouble() < 0.004)
            {
                high *= 1.02;
                low *= 0.98;
            }

            dense.Add(new Bar(DayBuilder.Epoch(date, minute), open, high, low, close, 500 + random.Next(4500)));
        }

        return close;
    }

    private static List<int> PickCandidates(Random random, List<Bar> dense)
    {
        var count = Math.Min(6, 2 + random.Next(5) + random.Next(2));   // 2 to 6, leaning high so that enough outcomes are compared
        var chosen = new HashSet<int>();
        for (var attempt = 0; chosen.Count < count && attempt < 200; attempt++)
        {
            var roll = random.NextDouble();
            if (roll < 0.25)
            {
                chosen.Add(NearSquareOff(random, dense));
            }
            else if (roll < 0.35)
            {
                chosen.Add(LastMinuteOfASession(random, dense));
            }
            else
            {
                chosen.Add(random.Next(dense.Count));
            }
        }

        return [.. chosen.Order()];
    }

    /// <summary>A minute whose end is within three minutes of a square-off (either of the two used), so that the fill minute is at or just before it.</summary>
    private static int NearSquareOff(Random random, List<Bar> dense)
    {
        var squareOff = SquareOffs[random.Next(SquareOffs.Length)];
        var wanted = squareOff - 1 - random.Next(3);
        var candidates = Enumerable.Range(0, dense.Count).Where(i => SessionClock.MinuteOfDay(dense[i].EpochSeconds) == wanted).ToList();
        return candidates.Count == 0 ? random.Next(dense.Count) : candidates[random.Next(candidates.Count)];
    }

    private static int LastMinuteOfASession(Random random, List<Bar> dense)
    {
        var lasts = Enumerable.Range(0, dense.Count)
            .Where(i => i == dense.Count - 1 || SessionClock.DateOf(dense[i + 1].EpochSeconds) != SessionClock.DateOf(dense[i].EpochSeconds))
            .ToList();
        return lasts[random.Next(lasts.Count)];
    }

    private static EntryCandidate MakeCandidate(Random random, List<Bar> dense, int position)
    {
        var bar = dense[position];
        var side = random.NextDouble() < 0.5 ? Side.Buy : Side.Sell;
        var distance = 0.005 + (random.NextDouble() * 0.025);
        var stop = side == Side.Buy ? bar.Close * (1 - distance) : bar.Close * (1 + distance);
        var trigger = new Bar(bar.EpochSeconds - (14 * 60), bar.Open, bar.High, bar.Low, bar.Close, 15 * 1_000L);
        return new EntryCandidate("ORB", Cand.Test, side, position, bar.EpochSeconds + 60, Timeframe.M15, trigger, 15, TriggerBarComplete: true, stop, DecisionTrace.Empty);
    }

    private static WalkRules MakeRules(Random random) => new(RewardRisks[random.Next(RewardRisks.Length)], SquareOffs[random.Next(SquareOffs.Length)]);

    /// <summary>Opens the minute after some decisions beyond the stop, so that a fill through the stop occurs.</summary>
    private static void PlantGapsAfterDecisions(Random random, List<Bar> dense, List<(int DenseIndex, EntryCandidate Candidate, WalkRules Rules)> walks)
    {
        foreach (var (index, candidate, _) in walks)
        {
            if (index + 1 >= dense.Count || random.NextDouble() >= 0.2)
            {
                continue;
            }

            var next = dense[index + 1];
            var open = candidate.Side == Side.Buy ? candidate.StopLevel * 0.999 : candidate.StopLevel * 1.001;
            dense[index + 1] = next with { Open = open, High = Math.Max(next.High, open), Low = Math.Min(next.Low, open) };
        }
    }

    /// <summary>Removes runs of minutes (never a candidate's own minute): random holes, and holes right after a decision.</summary>
    private static void PlantHoles(Random random, List<Bar> dense, HashSet<int> protectedIndexes, HashSet<int> removed, List<int> decisions)
    {
        var runs = random.Next(4);
        for (var r = 0; r < runs; r++)
        {
            var start = random.Next(dense.Count);
            var length = random.NextDouble() < 0.1 ? 40 : 1 + random.Next(8);
            Remove(removed, protectedIndexes, dense.Count, start, length);
        }

        foreach (var decision in decisions)
        {
            if (random.NextDouble() < 0.3)
            {
                Remove(removed, protectedIndexes, dense.Count, decision + 1, 1 + random.Next(8));
            }
        }
    }

    private static void Remove(HashSet<int> removed, HashSet<int> protectedIndexes, int count, int start, int length)
    {
        for (var i = start; i < start + length && i < count; i++)
        {
            if (!protectedIndexes.Contains(i))
            {
                removed.Add(i);
            }
        }
    }
}

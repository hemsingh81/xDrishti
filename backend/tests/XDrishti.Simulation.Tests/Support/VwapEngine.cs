using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The engine side of the comparison with the reference loop of VWAP_PULLBACK: one run through <c>Simulator.Run</c> with exact fills, and the differences against what the loop found.</summary>
internal static class VwapEngine
{
    /// <summary>The engine's result on the bars (the 0.1 % limit, no slippage, tick 0.05, costs none).</summary>
    public static SimulationResult Run(Bar[] bars, double pullAtr = 0.5, double rewardRisk = 2.0) =>
        Sim.Run(VwapPullback.Create(pullAtr, rewardRisk), bars, fills: Cand.NoSlippage);

    /// <summary>The candidates of the engine as the loop writes them: (side, decision index, stop).</summary>
    public static List<string> Candidates(SimulationResult result) =>
        [.. result.Outcomes.Select(o => string.Create(CultureInfo.InvariantCulture, $"{o.Candidate.Side} {o.Candidate.DecisionBarIndex} stop={Round(o.Candidate.StopLevel)}"))];

    /// <summary>The trades of the engine, one line each, in the form of <see cref="Lines(ReferenceLoop.Found)"/>.</summary>
    public static List<string> Trades(SimulationResult result) =>
        [.. result.Outcomes.Where(o => o.Walk.Trade is not null).Select(o =>
        {
            var t = o.Walk.Trade!;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{t.Side} {o.Candidate.DecisionBarIndex} entry@{t.EntryEpochSeconds}={Round(t.EntryPrice)} stop={Round(t.StopPrice)} target={Round(t.TargetPrice!.Value)} exit@{t.ExitEpochSeconds}={Round(t.ExitPrice)} {t.ExitReason} R={Round(t.RExact)}");
        })];

    /// <summary>The loop's candidates and trades as lines, comparable to <see cref="Candidates"/> and <see cref="Trades"/>.</summary>
    public static (List<string> Candidates, List<string> Trades) Lines(ReferenceLoop.Found found) =>
    (
        [.. found.Candidates.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Side} {c.DecisionIndex} stop={Round((double)c.Stop)}"))],
        [.. found.Trades.Select(t => string.Create(
            CultureInfo.InvariantCulture,
            $"{t.Side} {t.DecisionIndex} entry@{t.EntryEpoch}={Round((double)t.EntryPrice)} stop={Round((double)t.Stop)} target={Round((double)t.Target)} exit@{t.ExitEpoch}={Round((double)t.ExitPrice)} {t.Reason} R={Round((double)t.R)}"))]
    );

    /// <summary>True when the engine and the loop disagree on the list (candidates or trades).</summary>
    public static bool Differs(Bar[] bars, double pullAtr = 0.5, ReferenceLoop.Mutant mutant = ReferenceLoop.Mutant.None) => DaysDiffering(bars, pullAtr, mutant) > 0;

    /// <summary>
    /// The number of dates on which the engine and the loop disagree: a candidate or a trade of the date is in one list and not in the other, or a condition was true on a different number of the window steps of the date (the loop counts
    /// the five conditions of each side as the scanner does, independently of each other, from the first fixture day on).
    /// </summary>
    public static int DaysDiffering(Bar[] bars, double pullAtr = 0.5, ReferenceLoop.Mutant mutant = ReferenceLoop.Mutant.None)
    {
        var engine = Run(bars, pullAtr);
        var found = ReferenceLoop.Run(bars, pullAtr, mutant: mutant);
        var (loopCandidates, loopTrades) = Lines(found);
        var engineLines = Candidates(engine).Select((l, i) => (Date(engine.Outcomes[i].Candidate.DecisionEpochSeconds), "c " + l))
            .Concat(Trades(engine).Select((l, i) => (Date(engine.Outcomes.Where(o => o.Walk.Trade is not null).ElementAt(i).Candidate.DecisionEpochSeconds), "t " + l)));
        var loopLines = loopCandidates.Select((l, i) => (Date(bars[found.Candidates[i].DecisionIndex].EpochSeconds), "c " + l))
            .Concat(loopTrades.Select((l, i) => (Date(bars[found.Trades[i].DecisionIndex].EpochSeconds), "t " + l)));
        var engineCounts = EngineCounts(bars, pullAtr);
        var loopCounts = found.Counts.Where(c => c.Key.Date >= VwapDays.Date).Select(c => (c.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "k " + c.Key.Path + " T=" + c.Value.True + " F=" + c.Value.False));
        var left = engineLines.Concat(engineCounts).ToHashSet();
        var right = loopLines.Concat(loopCounts).ToHashSet();
        Last = left.Except(right).Select(x => "engine " + x.Item1 + " " + x.Item2).Concat(right.Except(left).Select(x => "loop   " + x.Item1 + " " + x.Item2)).ToList();
        return left.Except(right).Concat(right.Except(left)).Select(x => x.Item1).Distinct().Count();
    }

    /// <summary>The differences found by the last <see cref="DaysDiffering"/> call on this thread (for the message of a failing test).</summary>
    [ThreadStatic]
    public static List<string>? Last;

    /// <summary>The scanner's counters of the days from the first fixture day on, per date and path, as "k path T=n F=n" (the day's own steps: the difference of the totals at its two ends); a count of unknown or not-ready steps is part of the text.</summary>
    public static List<(string Date, string Text)> EngineCounts(Bar[] bars, double pullAtr = 0.5)
    {
        var lines = new List<(string, string)>();
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var scanner = Scan.Scanner(Scan.Compile(VwapPullback.Create(pullAtr), side));
            var before = scanner.Counters;
            for (var i = 0; i < bars.Length; i++)
            {
                scanner.Step(bars[i]);
                if (i + 1 < bars.Length && Date(bars[i + 1].EpochSeconds) == Date(bars[i].EpochSeconds))
                {
                    continue;
                }

                var after = scanner.Counters;
                if (string.CompareOrdinal(Date(bars[i].EpochSeconds), VwapDays.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0)
                {
                    foreach (var condition in after.Conditions)
                    {
                        var b = before.Conditions.FirstOrDefault(x => x.Path == condition.Path);
                        var (t, f, u, n) = (condition.True - (b?.True ?? 0), condition.False - (b?.False ?? 0), condition.Unknown - (b?.Unknown ?? 0), condition.NotReady - (b?.NotReady ?? 0));
                        lines.Add((Date(bars[i].EpochSeconds), "k " + condition.Path + " T=" + t + " F=" + f + (u + n > 0 ? " U=" + u + " N=" + n : string.Empty)));
                    }
                }

                before = after;
            }
        }

        return lines;
    }

    private static string Date(long epoch) => Render.Instant(epoch)[..10];

    private static string Round(double value) => (Math.Round(value, 6) + 0.0).ToString("R", CultureInfo.InvariantCulture);
}

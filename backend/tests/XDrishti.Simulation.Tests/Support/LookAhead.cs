using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The harness of the look-ahead property of the walker (AC8): an outcome that is <i>settled at</i> an index at most <c>k</c> must not change when the minutes after <c>k</c> are removed or replaced by extreme values
/// (epochs unchanged). A transform of the bars is applied to the full list and to each cut list on its own; the honest walker is run with the identity, the negative control with <see cref="Peek"/>.
/// </summary>
internal static class LookAhead
{
    /// <summary>The identity: the bars as they are.</summary>
    public static IReadOnlyList<Bar> Identity(IReadOnlyList<Bar> bars) => bars;

    /// <summary>
    /// A doctored input, not a mutated walker: every low becomes the lower of its own and the next minute's, every high the higher of its own and the next one's, so that a stop or target test that runs on this list peeks one
    /// minute ahead. The walker is a static function, so the control is built from its public API only.
    /// </summary>
    public static IReadOnlyList<Bar> Peek(IReadOnlyList<Bar> bars)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var next = i + 1 < bars.Count ? bars[i + 1] : bars[i];
            result[i] = bars[i] with { Low = Math.Min(bars[i].Low, next.Low), High = Math.Max(bars[i].High, next.High) };
        }

        return result;
    }

    /// <summary>The index at which an outcome was decided: the exit minute of a trade; the fill minute of a no-trade, or the candidate's own minute when none follows.</summary>
    public static int SettledAt(EntryCandidate candidate, IReadOnlyList<Bar> bars, WalkOutcome outcome)
    {
        if (outcome.Trade is null)
        {
            return candidate.DecisionBarIndex + 1 < bars.Count ? candidate.DecisionBarIndex + 1 : candidate.DecisionBarIndex;
        }

        for (var i = candidate.DecisionBarIndex; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds == outcome.Trade.ExitEpochSeconds)
            {
                return i;
            }
        }

        throw new InvalidOperationException("The exit minute of a trade is not in the list.");
    }

    /// <summary>The full-run outcomes of every walk of the run.</summary>
    public static List<(SeededWalk Walk, WalkOutcome Outcome, int Settled)> FullRun(SeededRun run, Func<IReadOnlyList<Bar>, IReadOnlyList<Bar>> transform)
    {
        var bars = transform(run.Bars);
        var results = new List<(SeededWalk, WalkOutcome, int)>();
        foreach (var walk in run.Walks)
        {
            var outcome = Ok(TradeWalker.Walk(walk.Candidate, bars, walk.Rules));
            results.Add((walk, outcome, SettledAt(walk.Candidate, bars, outcome)));
        }

        return results;
    }

    /// <summary>A random cut for the run, from the seed, in the last tenth of the list (so that most outcomes are settled before it and are compared).</summary>
    public static int CutPoint(SeededRun run)
    {
        var random = new Random((run.Seed * 7919) + 13);
        var from = run.Bars.Count * 9 / 10;
        return from + random.Next(run.Bars.Count - from);
    }

    /// <summary>
    /// The cut of the negative control: half the time one minute before the exit of a random stop or target exit (the place where a walker that peeks one minute ahead would act earlier), otherwise <see cref="CutPoint"/>.
    /// It is chosen from the honest run; the honest walker is also run at these cuts, on the very same inputs.
    /// </summary>
    public static int PeekCutPoint(SeededRun run)
    {
        var random = new Random((run.Seed * 104_729) + 7);
        var exits = FullRun(run, Identity).Where(h => h.Outcome.Trade is { ExitReason: ExitReason.Stop or ExitReason.Target } && h.Settled > h.Walk.Candidate.DecisionBarIndex + 1).ToList();
        return exits.Count > 0 && random.NextDouble() < 0.5 ? exits[random.Next(exits.Count)].Settled - 1 : CutPoint(run);
    }

    /// <summary>The bars after the cut point <paramref name="k"/> replaced by zero, huge and mixed extreme prices and volumes; the epochs stay.</summary>
    public static IReadOnlyList<Bar> ReplaceAfter(IReadOnlyList<Bar> bars, int k)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            result[i] = i <= k ? bars[i] : (i % 3) switch
            {
                0 => new Bar(bars[i].EpochSeconds, 0, 0, 0, 0, 0),
                1 => new Bar(bars[i].EpochSeconds, 1e15, 1e15, 1e15, 1e15, long.MaxValue),
                _ => new Bar(bars[i].EpochSeconds, 0, 1e15, 0, 1e15, long.MaxValue),
            };
        }

        return result;
    }

    /// <summary>
    /// Compares, for the cut <paramref name="k"/>, every outcome of the full run that is settled at an index at most <paramref name="k"/> with the same candidate walked over the list cut there (removed, then replaced).
    /// Returns the violations (empty for an honest walker) and how many outcomes were compared.
    /// </summary>
    public static (List<string> Violations, int Compared) Check(SeededRun run, int k, Func<IReadOnlyList<Bar>, IReadOnlyList<Bar>> transform)
    {
        var violations = new List<string>();
        var compared = 0;
        var full = FullRun(run, transform);
        var removed = transform(run.Bars.Take(k + 1).ToList());
        var replaced = transform(ReplaceAfter(run.Bars, k));
        foreach (var (walk, outcome, settled) in full)
        {
            if (settled > k)
            {
                continue;
            }

            compared++;
            var expected = OutcomeFormatter.Render(Result.Success(outcome));
            foreach (var (name, list) in new[] { ("removed", removed), ("replaced", replaced) })
            {
                var cut = OutcomeFormatter.Render(TradeWalker.Walk(walk.Candidate, list, walk.Rules));
                if (cut != expected)
                {
                    violations.Add($"seed {run.Seed} k {k} candidate {walk.Candidate.DecisionBarIndex} {name}: {expected} <> {cut}");
                }
            }
        }

        return (violations, compared);
    }

    private static WalkOutcome Ok(Result<WalkOutcome> result)
    {
        result.IsSuccess.ShouldBeTrue(result.Error.Code);
        return result.Value;
    }
}

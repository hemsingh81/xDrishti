using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Replay.Comparison;

/// <summary>
/// SR-01 part D: pairs the daily returns of a frozen and a learning replay (same range, scope and settings) by session and hands them to the existing <see cref="ThreeArmLoopTest.Compare"/> (the paired day bootstrap of CF-10c, ADR 0216) with the loop's settings
/// (<see cref="LoopOptions"/>: draws, level, minimum days, helps-at cut, hurts bar). Helps is <c>learning_better</c>, Hurts is <c>frozen_better</c>; that test computes both directions with one seed and the same rule as the loop report, so there is no second rule here. Seeded from the range, so the same inputs repeat. Pure.
/// Equity is capital plus realised result, so a day on which neither run exited a trade has a difference of exactly 0: fewer than the minimum of days with a non-zero difference is <c>too_little_history</c>, however many sessions were paired.
/// </summary>
public static class ReplayPairComparison
{
    /// <summary>The failure code for series that cannot be paired.</summary>
    public const string MisalignedCode = "replay.pair_misaligned";

    /// <summary>The failure code for loop options out of range.</summary>
    public const string InvalidCode = "replay.pair_invalid";

    /// <summary>Compares the runs. Both series are one entry per session, in order, from the same start capital.</summary>
    public static Result<ReplayPairResult> Compare(IReadOnlyList<ReplayDayEquity> frozen, IReadOnlyList<ReplayDayEquity> learning, decimal capital, LoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(learning);
        options ??= new LoopOptions();
        if (options.Problems().Count > 0)
        {
            return Result.Failure<ReplayPairResult>(Error.Validation(InvalidCode, string.Join("; ", options.Problems())));
        }

        if (capital <= 0 || frozen.Count == 0 || frozen.Count != learning.Count)
        {
            return Misaligned("both runs need the same non-zero number of sessions and a capital above 0");
        }

        var f = new double[frozen.Count];
        var l = new double[frozen.Count];
        var days = new DateOnly[frozen.Count];
        decimal prevF = capital, prevL = capital;
        for (var i = 0; i < f.Length; i++)
        {
            if (frozen[i].Session != learning[i].Session || (i > 0 && frozen[i].Session <= frozen[i - 1].Session))
            {
                return Misaligned("the sessions of the two runs differ or are not in order");
            }

            if (frozen[i].Equity <= 0 || learning[i].Equity <= 0)
            {
                return Misaligned("a run's equity is not above 0");
            }

            f[i] = (double)((frozen[i].Equity / prevF) - 1);
            l[i] = (double)((learning[i].Equity / prevL) - 1);
            days[i] = frozen[i].Session;
            (prevF, prevL) = (frozen[i].Equity, learning[i].Equity);
        }

        var mean = Enumerable.Range(0, f.Length).Average(i => l[i] - f[i]);

        // shortcut: realised-only equity; upgrade to mark-to-market once closes are stored
        var informative = Enumerable.Range(0, f.Length).Count(i => l[i] != f[i]);
        if (informative < options.MinDays)
        {
            return Result.Success(new ReplayPairResult(PairAnswer.TooLittleHistory, f.Length, informative, mean, null, null, null, null, options.HelpsAt, LoopOptions.HurtsBar, options.MinDays));
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"sr01|{days[0]:yyyy-MM-dd}|{days[^1]:yyyy-MM-dd}|{days.Length}");
        var arm = ThreeArmLoopTest.Compare("learning_vs_frozen", l, f, days, options.MinDays, options.Draws, options.Level, options.HelpsAt, LoopOptions.HurtsBar, key);
        PairAnswer? answer = arm.Verdict switch
        {
            LoopVerdict.Helps => PairAnswer.LearningBetter,
            LoopVerdict.Hurts => PairAnswer.FrozenBetter,
            LoopVerdict.NoMeasurableDifference => PairAnswer.NoMeasurableDifference,
            LoopVerdict.Inconclusive => PairAnswer.TooLittleHistory,
            _ => null,
        };
        return answer is { } a
            ? Result.Success(new ReplayPairResult(a, f.Length, informative, mean, arm.ProbabilityBetter, arm.ProbabilityWorse, arm.Low, arm.High, options.HelpsAt, LoopOptions.HurtsBar, options.MinDays))
            : Result.Failure<ReplayPairResult>(Error.Validation(InvalidCode, arm.Text));
    }

    private static Result<ReplayPairResult> Misaligned(string message) => Result.Failure<ReplayPairResult>(Error.Validation(MisalignedCode, message));
}

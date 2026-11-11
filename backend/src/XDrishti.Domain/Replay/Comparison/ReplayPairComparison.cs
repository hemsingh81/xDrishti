using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Replay.Comparison;

/// <summary>The close-of-session equity of one replay on one session.</summary>
public readonly record struct ReplayDayEquity(DateOnly Session, decimal Equity);

/// <summary>The answer of the frozen-versus-learning comparison (SR-01 AC 9).</summary>
public enum PairAnswer
{
    /// <summary>The learning run beat the frozen run with probability at or above the cut.</summary>
    LearningBetter,

    /// <summary>The frozen run beat the learning run with probability at or above the cut.</summary>
    FrozenBetter,

    /// <summary>Neither side reached the cut.</summary>
    NoMeasurableDifference,

    /// <summary>Fewer paired days than the bootstrap's minimum.</summary>
    TooLittleHistory,
}

/// <summary>The comparison: always n, the mean daily difference (learning minus frozen, as a fraction of equity) and the cut; the probabilities are null when there is too little history.</summary>
/// <param name="Answer">The answer.</param>
/// <param name="Days">Paired days (n).</param>
/// <param name="MeanDailyDifference">Mean of learning daily return minus frozen daily return.</param>
/// <param name="ProbabilityLearningBetter">P(mean difference above 0) from the paired day bootstrap.</param>
/// <param name="ProbabilityFrozenBetter">The same for the mirrored difference.</param>
/// <param name="Cut">The probability a side needs (the loop's helps-at bar).</param>
/// <param name="MinDays">The fewest days the bootstrap accepts.</param>
public sealed record ReplayPairResult(PairAnswer Answer, int Days, double MeanDailyDifference, double? ProbabilityLearningBetter, double? ProbabilityFrozenBetter, double Cut, int MinDays)
{
    /// <summary>The wire word of the answer.</summary>
    public string AnswerCode => Answer switch
    {
        PairAnswer.LearningBetter => "learning_better",
        PairAnswer.FrozenBetter => "frozen_better",
        PairAnswer.NoMeasurableDifference => "no_measurable_difference",
        _ => "too_little_history",
    };
}

/// <summary>
/// SR-01 part D: pairs the daily returns of a frozen and a learning replay (same range, scope and settings) by session and asks the existing <see cref="PairedDayBootstrap"/> (CF-10c, ADR 0216) with the loop's settings (<see cref="LoopOptions"/>: draws, level, minimum days, helps-at cut).
/// Same rule as <see cref="ThreeArmLoopTest"/>: learning better when P(better) is at or above the cut, frozen better when the mirrored P is, otherwise no measurable difference. Seeded from the range, so the same inputs repeat. Pure.
/// </summary>
public static class ReplayPairComparison
{
    /// <summary>The failure code for series that cannot be paired.</summary>
    public const string MisalignedCode = "replay.pair_misaligned";

    /// <summary>Compares the runs. Both series are one entry per session, in order, from the same start capital.</summary>
    public static Result<ReplayPairResult> Compare(IReadOnlyList<ReplayDayEquity> frozen, IReadOnlyList<ReplayDayEquity> learning, decimal capital, LoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(learning);
        options ??= new LoopOptions();
        if (capital <= 0 || frozen.Count == 0 || frozen.Count != learning.Count)
        {
            return Misaligned("both runs need the same non-zero number of sessions and a capital above 0");
        }

        var gains = new double[frozen.Count];
        var days = new DateOnly[frozen.Count];
        decimal prevF = capital, prevL = capital;
        for (var i = 0; i < gains.Length; i++)
        {
            if (frozen[i].Session != learning[i].Session || (i > 0 && frozen[i].Session <= frozen[i - 1].Session))
            {
                return Misaligned("the sessions of the two runs differ or are not in order");
            }

            if (prevF <= 0 || prevL <= 0)
            {
                return Misaligned("a run's equity is not above 0");
            }

            gains[i] = (double)((learning[i].Equity / prevL) - (frozen[i].Equity / prevF));
            days[i] = frozen[i].Session;
            (prevF, prevL) = (frozen[i].Equity, learning[i].Equity);
        }

        var mean = gains.Average();
        if (gains.Length < options.MinDays)
        {
            return Result.Success(new ReplayPairResult(PairAnswer.TooLittleHistory, gains.Length, mean, null, null, options.HelpsAt, options.MinDays));
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"sr01|{days[0]:yyyy-MM-dd}|{days[^1]:yyyy-MM-dd}|{days.Length}");
        var better = PairedDayBootstrap.Assess(gains, days, options.Draws, options.Level, key, options.MinDays);
        if (better.IsFailure)
        {
            return Result.Failure<ReplayPairResult>(better.Error);
        }

        var worse = PairedDayBootstrap.Assess([.. gains.Select(g => -g)], days, options.Draws, options.Level, key, options.MinDays);
        if (worse.IsFailure)
        {
            return Result.Failure<ReplayPairResult>(worse.Error);
        }

        var pB = better.Value.ProbabilityBetter;
        var pW = worse.Value.ProbabilityBetter;
        var answer = pB >= options.HelpsAt ? PairAnswer.LearningBetter : pW >= options.HelpsAt ? PairAnswer.FrozenBetter : PairAnswer.NoMeasurableDifference;
        return Result.Success(new ReplayPairResult(answer, gains.Length, mean, pB, pW, options.HelpsAt, options.MinDays));
    }

    private static Result<ReplayPairResult> Misaligned(string message) => Result.Failure<ReplayPairResult>(Error.Validation(MisalignedCode, message));
}

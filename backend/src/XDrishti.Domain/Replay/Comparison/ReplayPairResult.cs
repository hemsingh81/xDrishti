namespace XDrishti.Domain.Replay.Comparison;

/// <summary>The comparison: always n, the informative-day count, the mean daily difference (learning minus frozen, as a fraction of equity) and the cuts; the probabilities and interval are null when there is too little history.</summary>
/// <param name="Answer">The answer.</param>
/// <param name="Days">Paired days (n).</param>
/// <param name="InformativeDays">Paired days on which the two runs differ (a day on which neither run exited a trade carries no information).</param>
/// <param name="MeanDailyDifference">Mean of learning daily return minus frozen daily return over all paired days.</param>
/// <param name="ProbabilityLearningBetter">P(mean difference above 0) from the paired day bootstrap.</param>
/// <param name="ProbabilityFrozenBetter">P(mean difference below 0).</param>
/// <param name="Low">Lower end of the bootstrap interval of the mean difference.</param>
/// <param name="High">Upper end of the bootstrap interval of the mean difference.</param>
/// <param name="Cut">The probability learning needs (the loop's helps-at bar).</param>
/// <param name="HurtsBar">The probability frozen needs.</param>
/// <param name="MinDays">The fewest days (paired, and informative) the answer needs.</param>
public sealed record ReplayPairResult(PairAnswer Answer, int Days, int InformativeDays, double MeanDailyDifference, double? ProbabilityLearningBetter, double? ProbabilityFrozenBetter, double? Low, double? High, double Cut, double HurtsBar, int MinDays)
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

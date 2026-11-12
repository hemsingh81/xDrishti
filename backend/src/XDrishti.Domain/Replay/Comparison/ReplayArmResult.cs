namespace XDrishti.Domain.Replay.Comparison;

/// <summary>The answer of the selection-versus-baseline comparison (SR-02, D2): the same rule and bars as the frozen-versus-learning pair.</summary>
public enum ArmAnswer
{
    /// <summary>The selection run beat the no-selection baseline with probability at or above the cut.</summary>
    SelectionBetter,

    /// <summary>The baseline beat the selection run with probability at or above the hurts bar.</summary>
    BaselineBetter,

    /// <summary>Neither side reached its bar.</summary>
    NoMeasurableDifference,

    /// <summary>Fewer paired days, or fewer days with a non-zero difference, than the minimum.</summary>
    TooLittleHistory,
}

/// <summary>The comparison of the selection arm with the baseline arm: always n, the informative days and the mean daily difference (selection minus baseline); the probabilities and the interval are null when there is too little history.</summary>
public sealed record ReplayArmResult(ArmAnswer Answer, int Days, int InformativeDays, double MeanDailyDifference, double? ProbabilitySelectionBetter, double? ProbabilityBaselineBetter, double? Low, double? High, double Cut, double HurtsBar, int MinDays)
{
    /// <summary>The wire word of the answer.</summary>
    public string AnswerCode => Answer switch
    {
        ArmAnswer.SelectionBetter => "selection_better",
        ArmAnswer.BaselineBetter => "baseline_better",
        ArmAnswer.NoMeasurableDifference => "no_measurable_difference",
        _ => "too_little_history",
    };
}

namespace XDrishti.Domain.Learning;

/// <summary>What the comparison says about a candidate (P6-00 D4, D5, question 4).</summary>
public enum ValidationOutcome
{
    /// <summary>It beats both baselines by the margin in enough folds and pooled, is calibrated and had enough trades, on at least the minimum number of folds.</summary>
    Eligible,

    /// <summary>It cannot beat the baselines (or misses calibration or sample): the system keeps the cell statistics.</summary>
    CannotBeatBaselines,

    /// <summary>Every rule passes but there are fewer folds than the minimum: not proven, never "eligible".</summary>
    Inconclusive,
}

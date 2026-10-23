namespace XDrishti.Domain.Learning;

/// <summary>What the phase check says about one exit criterion (P6-18, ADR 0080). Met is the only one that may be read as a pass, and it needs evidence the data can carry.</summary>
public enum PhaseVerdict
{
    /// <summary>The stored records show the criterion is satisfied, with enough folds, a kept hold-out and a measured time where one is needed.</summary>
    Met = 0,

    /// <summary>The stored records show the criterion is not satisfied.</summary>
    NotMet = 1,

    /// <summary>The records are missing, or too thin, or the quantity was not measured: no call is made.</summary>
    NotEvaluable = 2,
}

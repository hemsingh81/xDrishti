namespace XDrishti.Domain.Learning;

/// <summary>What an experiment says about a knob change (P6-11, doc 09 §12). Only <see cref="Better"/> may lead to a shadow trial.</summary>
public enum ExperimentOutcome
{
    /// <summary>Better in enough folds and pooled, by more than the required improvement after the trials, without a total-R drop, on at least the minimum number of folds.</summary>
    Better,

    /// <summary>Not better: the numbers do not clear a rule. The reasons say which.</summary>
    NotBetter,

    /// <summary>Every rule that could be checked passes but fewer folds than the minimum exist, or none is usable: not proven, never "better".</summary>
    Inconclusive,

    /// <summary>The candidate assumes cheaper slippage than the baseline: it cannot win, whatever the numbers (doc 09 §12 rule 3).</summary>
    CheaperAssumption,
}

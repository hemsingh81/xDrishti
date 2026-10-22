namespace XDrishti.Domain.Lab;

/// <summary>The verdict of doc 11 section 4.</summary>
public enum VerdictOutcome
{
    /// <summary>Every gate met and no honest limit applies.</summary>
    Pass,

    /// <summary>Not enough to pass, not bad enough to fail.</summary>
    Marginal,

    /// <summary>A gate missed by more than the margin, or too few trades to judge.</summary>
    Fail,
}

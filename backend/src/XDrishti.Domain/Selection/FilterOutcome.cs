namespace XDrishti.Domain.Selection;

/// <summary>The answer of one eligibility filter (doc 10 section 2, spec P7-01). <see cref="Unavailable"/> means the input is missing or not valid as of the decision date; it is never a pass.</summary>
public enum FilterOutcome
{
    /// <summary>The number is on the right side of the limit.</summary>
    Pass,

    /// <summary>The number is on the wrong side of the limit.</summary>
    Fail,

    /// <summary>The input is missing, not finite or not known as of the decision date.</summary>
    Unavailable,
}

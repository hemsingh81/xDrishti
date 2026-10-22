namespace XDrishti.Domain.Learning;

/// <summary>A fitted scorer: the probability of label 1 for one row.</summary>
public interface IRowScorer
{
    /// <summary>The probability, finite and in [0, 1].</summary>
    double Score(ValidationRow row);
}

/// <summary>A fitted scorer that can say that its fit is not trustworthy; the harness prints the note in the report limits.</summary>
public interface IFitNote
{
    /// <summary>A sentence about the fit, or null when there is nothing to say.</summary>
    string? Note { get; }
}

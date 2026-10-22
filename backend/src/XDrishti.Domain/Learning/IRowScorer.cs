namespace XDrishti.Domain.Learning;

/// <summary>A fitted scorer: the probability of label 1 for one row.</summary>
public interface IRowScorer
{
    /// <summary>The probability, finite and in [0, 1].</summary>
    double Score(ValidationRow row);
}

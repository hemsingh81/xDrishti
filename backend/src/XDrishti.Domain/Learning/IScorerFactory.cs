namespace XDrishti.Domain.Learning;

/// <summary>
/// Something that learns from the training rows of a fold and scores rows (P6-05). The harness hands it only the train rows; a candidate model (P6-06) is adapted to this port, the baselines implement it here.
/// </summary>
public interface IScorerFactory
{
    /// <summary>The name in the report; unique among the scorers of one comparison.</summary>
    string Name { get; }

    /// <summary>Fits on the train rows (never the test rows) and returns the scorer. Deterministic.</summary>
    IRowScorer Fit(IReadOnlyList<ValidationRow> train);
}

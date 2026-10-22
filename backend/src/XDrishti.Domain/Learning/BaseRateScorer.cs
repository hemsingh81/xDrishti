namespace XDrishti.Domain.Learning;

/// <summary>The base-rate baseline: the same probability for every row, the train win rate with a Laplace correction <c>(wins + 1) / (n + 2)</c> so that a log loss is never infinite.</summary>
public sealed class BaseRateScorer : IScorerFactory
{
    /// <summary>The report name.</summary>
    public const string ScorerName = "base_rate";

    /// <inheritdoc />
    public string Name => ScorerName;

    /// <summary>The smoothed win rate of the rows.</summary>
    public static double Rate(IReadOnlyList<ValidationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return (rows.Count(r => r.Label == 1) + 1.0) / (rows.Count + 2.0);
    }

    /// <inheritdoc />
    public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Constant(Rate(train));

    private sealed class Constant(double p) : IRowScorer
    {
        public double Score(ValidationRow row) => p;
    }
}

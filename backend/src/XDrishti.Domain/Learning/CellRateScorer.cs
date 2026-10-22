namespace XDrishti.Domain.Learning;

/// <summary>
/// The cell-statistics baseline of doc 09 layer 1 (P6-05): the win rate of the row's cell in the train rows, shrunk toward the train base rate with a prior of <c>PriorStrength</c> pseudo-trades, <c>(wins + s r) / (n + s)</c>.
/// A cell not seen in training scores the base rate. No recency weights (the train rows are all older than the test window); the full Beta posterior of P6-01 needs the stored snapshot and arrives with the feature slice.
/// </summary>
/// <param name="PriorStrength">The pseudo-trades of the prior (doc 16 <c>bayes_prior.strength</c>, 20).</param>
public sealed class CellRateScorer(double PriorStrength = 20) : IScorerFactory
{
    /// <summary>The report name.</summary>
    public const string ScorerName = "cell_rate";

    /// <inheritdoc />
    public string Name => ScorerName;

    /// <inheritdoc />
    public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
    {
        ArgumentNullException.ThrowIfNull(train);
        var baseRate = BaseRateScorer.Rate(train);
        var cells = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in train.GroupBy(r => r.CellKey, StringComparer.Ordinal))
        {
            var n = group.Count();
            cells[group.Key] = (group.Count(r => r.Label == 1) + (PriorStrength * baseRate)) / (n + PriorStrength);
        }

        return new Scorer(cells, baseRate);
    }

    private sealed class Scorer(Dictionary<string, double> cells, double baseRate) : IRowScorer
    {
        public double Score(ValidationRow row) => cells.TryGetValue(row.CellKey, out var p) ? p : baseRate;
    }
}

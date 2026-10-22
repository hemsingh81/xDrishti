using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// A training set joined to its decision-time features (P6-04, ADR 0066). <see cref="Id"/> is a pure function of the label set, the feature catalog version, the required names and every row, so the same inputs give the same id.
/// </summary>
/// <param name="Id">SHA-256 (lower-case hex).</param>
/// <param name="LabelSetId">The id of the <see cref="TrainingSet"/> the labels came from.</param>
/// <param name="FeatureVersion">The catalog version.</param>
/// <param name="RuleVersion">The label rule version.</param>
/// <param name="Basis">The basis of every <see cref="TrainingRow.R"/>.</param>
/// <param name="RunKeys">The source runs.</param>
/// <param name="Required">The feature names a row had to have known.</param>
/// <param name="FromDate">Earliest trade date among the rows.</param>
/// <param name="ToDate">Latest trade date among the rows.</param>
/// <param name="Rows">The examples, ordered by trade key (ordinal).</param>
/// <param name="Labelled">Trades that had a label.</param>
/// <param name="DroppedNoFeatures">Labelled trades with no stored feature row.</param>
/// <param name="DroppedUnknown">Labelled trades with a feature row but a required feature unknown.</param>
/// <param name="UnknownByFeature">For each required feature, how many labelled trades had it unknown (a trade may count in several).</param>
public sealed record TrainingData(
    string Id,
    string LabelSetId,
    string FeatureVersion,
    string RuleVersion,
    LabelBasis Basis,
    IReadOnlyList<string> RunKeys,
    IReadOnlyList<string> Required,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TrainingRow> Rows,
    int Labelled,
    int DroppedNoFeatures,
    int DroppedUnknown,
    IReadOnlyDictionary<string, int> UnknownByFeature)
{
    /// <summary>Rows with label 1.</summary>
    public int Positives => Rows.Count(r => r.Label == 1);

    /// <summary>Rows with label 0.</summary>
    public int Negatives => Rows.Count - Positives;
}

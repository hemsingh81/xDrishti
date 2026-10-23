namespace XDrishti.Domain.Learning;

/// <summary>
/// Isotonic (pool-adjacent-violators) calibration of a score into a probability (P6-06, ADR 0068): the fitted map is non-decreasing, so it never reorders rows. Equal scores are one point; neighbouring blocks whose mean label does not rise are pooled;
/// the knots are the pooled blocks (mean score, mean label) and <see cref="Apply"/> interpolates linearly between them and is flat outside. Sequential sums only, so the same input gives the same knots. Fit it on rows the scorer was not trained on.
/// </summary>
public sealed class IsotonicCalibrator
{
    private readonly double[] _scores;
    private readonly double[] _values;

    private IsotonicCalibrator(double[] scores, double[] values)
    {
        _scores = scores;
        _values = values;
    }

    /// <summary>The knot scores, strictly increasing.</summary>
    public IReadOnlyList<double> Scores => _scores;

    /// <summary>The knot probabilities, non-decreasing, in [0, 1].</summary>
    public IReadOnlyList<double> Values => _values;

    /// <summary>Fits the map; non-finite scores are skipped; with no usable pair the map is the constant 0.5.</summary>
    /// <param name="scores">The raw scores.</param>
    /// <param name="labels">The labels, 0 or 1, one per score.</param>
    public static IsotonicCalibrator Fit(IReadOnlyList<double> scores, IReadOnlyList<int> labels)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(labels);
        if (scores.Count != labels.Count)
        {
            throw new ArgumentException("scores and labels must have the same length.", nameof(labels));
        }

        var order = Enumerable.Range(0, scores.Count).Where(i => double.IsFinite(scores[i])).OrderBy(i => scores[i]).ThenBy(i => i).ToArray();
        if (order.Length == 0)
        {
            return new IsotonicCalibrator([0], [0.5]);
        }

        // one block per distinct score, then pool while the mean label does not rise
        var blocks = new List<Block>();
        foreach (var i in order)
        {
            var next = new Block(1, labels[i] == 1 ? 1 : 0, scores[i], scores[i]);
            if (blocks.Count > 0 && blocks[^1].LastScore == scores[i])
            {
                blocks[^1] = blocks[^1].Merge(next);
            }
            else
            {
                blocks.Add(next);
            }

            while (blocks.Count > 1 && blocks[^2].Mean >= blocks[^1].Mean)
            {
                var merged = blocks[^2].Merge(blocks[^1]);
                blocks.RemoveRange(blocks.Count - 2, 2);
                blocks.Add(merged);
            }
        }

        return new IsotonicCalibrator([.. blocks.Select(b => b.MeanScore)], [.. blocks.Select(b => b.Mean)]);
    }

    /// <summary>A map from stored knots (a model loaded from its artifact); the knots must be strictly increasing in score and non-decreasing in value within [0, 1].</summary>
    public static IsotonicCalibrator FromKnots(IReadOnlyList<double> scores, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(values);
        if (scores.Count == 0 || scores.Count != values.Count)
        {
            throw new ArgumentException("knots must be non-empty and of equal length.", nameof(values));
        }

        for (var i = 0; i < scores.Count; i++)
        {
            var bad = !double.IsFinite(scores[i]) || !double.IsFinite(values[i]) || values[i] is < 0 or > 1
                || (i > 0 && (scores[i] <= scores[i - 1] || values[i] < values[i - 1]));
            if (bad)
            {
                throw new ArgumentException("knots are not a monotone map.", nameof(values));
            }
        }

        return new IsotonicCalibrator([.. scores], [.. values]);
    }

    /// <summary>The calibrated probability of a raw score; a non-finite score reads as the middle knot.</summary>
    public double Apply(double score)
    {
        if (!double.IsFinite(score))
        {
            return _values[_values.Length / 2];
        }

        if (score <= _scores[0])
        {
            return _values[0];
        }

        if (score >= _scores[^1])
        {
            return _values[^1];
        }

        var hi = Array.BinarySearch(_scores, score);
        if (hi >= 0)
        {
            return _values[hi];
        }

        hi = ~hi;
        var lo = hi - 1;
        var t = (score - _scores[lo]) / (_scores[hi] - _scores[lo]);
        return _values[lo] + (t * (_values[hi] - _values[lo]));
    }

    private readonly record struct Block(int Count, int Wins, double SumScores, double LastScore)
    {
        public double Mean => (double)Wins / Count;

        public double MeanScore => SumScores / Count;

        public Block Merge(Block other) => new(Count + other.Count, Wins + other.Wins, SumScores + other.SumScores, other.LastScore);
    }
}

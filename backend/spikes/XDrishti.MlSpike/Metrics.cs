namespace XDrishti.MlSpike;

/// <summary>Binary-classification metrics implemented from scratch so they can be unit-tested against hand-computed cases.</summary>
public static class Metrics
{
    /// <summary>Area under the ROC curve (Mann–Whitney U with average ranks for ties).</summary>
    public static double Auc(IReadOnlyList<double> scores, IReadOnlyList<bool> labels)
    {
        var n = scores.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => scores[i]).ToArray();
        var ranks = new double[n];
        for (var i = 0; i < n;)
        {
            var j = i;
            while (j + 1 < n && scores[order[j + 1]] == scores[order[i]])
            {
                j++;
            }

            var average = ((i + j) / 2.0) + 1;
            for (var k = i; k <= j; k++)
            {
                ranks[order[k]] = average;
            }

            i = j + 1;
        }

        double positives = 0, rankSum = 0;
        for (var i = 0; i < n; i++)
        {
            if (labels[i])
            {
                positives++;
                rankSum += ranks[i];
            }
        }

        var negatives = n - positives;
        return positives == 0 || negatives == 0 ? double.NaN : (rankSum - (positives * (positives + 1) / 2.0)) / (positives * negatives);
    }

    public static double LogLoss(IReadOnlyList<double> probabilities, IReadOnlyList<bool> labels)
    {
        const double Eps = 1e-15;
        var sum = 0.0;
        for (var i = 0; i < probabilities.Count; i++)
        {
            var p = Math.Clamp(probabilities[i], Eps, 1 - Eps);
            sum -= labels[i] ? Math.Log(p) : Math.Log(1 - p);
        }

        return sum / probabilities.Count;
    }

    public static double Brier(IReadOnlyList<double> probabilities, IReadOnlyList<bool> labels)
    {
        var sum = 0.0;
        for (var i = 0; i < probabilities.Count; i++)
        {
            var d = probabilities[i] - (labels[i] ? 1 : 0);
            sum += d * d;
        }

        return sum / probabilities.Count;
    }

    /// <summary>Expected calibration error: sample-weighted mean |mean predicted − observed rate| over equal-width probability bins.</summary>
    public static double Ece(IReadOnlyList<double> probabilities, IReadOnlyList<bool> labels, int bins = 10)
    {
        var count = new int[bins];
        var sumP = new double[bins];
        var sumY = new double[bins];
        for (var i = 0; i < probabilities.Count; i++)
        {
            var b = Math.Min(bins - 1, (int)(probabilities[i] * bins));
            count[b]++;
            sumP[b] += probabilities[i];
            sumY[b] += labels[i] ? 1 : 0;
        }

        var ece = 0.0;
        for (var b = 0; b < bins; b++)
        {
            if (count[b] > 0)
            {
                ece += count[b] * Math.Abs((sumP[b] / count[b]) - (sumY[b] / count[b]));
            }
        }

        return ece / probabilities.Count;
    }

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);
}

/// <summary>Isotonic regression by pool-adjacent-violators — our own implementation, used to cross-check ML.NET's calibrator.</summary>
public sealed class Pav
{
    private readonly double[] _upperBounds;
    private readonly double[] _values;

    private Pav(double[] upperBounds, double[] values)
    {
        _upperBounds = upperBounds;
        _values = values;
    }

    public static Pav Fit(IReadOnlyList<double> scores, IReadOnlyList<bool> labels)
    {
        var order = Enumerable.Range(0, scores.Count).OrderBy(i => scores[i]).ToArray();
        // Each block: (sum of labels, weight, largest score in block)
        var sums = new List<double>();
        var weights = new List<double>();
        var uppers = new List<double>();
        for (var k = 0; k < order.Length;)
        {
            // Equal scores must get one output, so they start as a single block.
            var end = k;
            var positives = 0.0;
            while (end < order.Length && scores[order[end]] == scores[order[k]])
            {
                positives += labels[order[end]] ? 1 : 0;
                end++;
            }

            sums.Add(positives);
            weights.Add(end - k);
            uppers.Add(scores[order[k]]);
            k = end;
            while (sums.Count > 1 && sums[^2] / weights[^2] >= sums[^1] / weights[^1])
            {
                var last = sums.Count - 1;
                sums[last - 1] += sums[last];
                weights[last - 1] += weights[last];
                uppers[last - 1] = uppers[last];
                sums.RemoveAt(last);
                weights.RemoveAt(last);
                uppers.RemoveAt(last);
            }
        }

        return new Pav([.. uppers], [.. sums.Select((s, k) => s / weights[k])]);
    }

    public double Apply(double score)
    {
        var index = Array.BinarySearch(_upperBounds, score);
        if (index < 0)
        {
            index = ~index;
        }

        return _values[Math.Min(index, _values.Length - 1)];
    }
}

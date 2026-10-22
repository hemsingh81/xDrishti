namespace XDrishti.Domain.Learning;

/// <summary>Pure metrics of predictions (P6-05). The input order breaks ties of equal scores, so the same input gives the same numbers.</summary>
public static class MetricsCalculator
{
    private const double Eps = 1e-15;

    /// <summary>The metrics of <paramref name="scores"/> against <paramref name="labels"/> (same length, at least one row) with <paramref name="bins"/> equal-mass calibration bins.</summary>
    public static ProbabilityMetrics Compute(IReadOnlyList<double> scores, IReadOnlyList<int> labels, int bins = 10)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(labels);
        if (scores.Count != labels.Count || scores.Count == 0 || bins < 1)
        {
            throw new ArgumentException("Scores and labels must be the same non-zero length and bins at least 1.");
        }

        var n = scores.Count;
        var positives = labels.Count(l => l == 1);
        double loss = 0, brier = 0;
        for (var i = 0; i < n; i++)
        {
            var p = Math.Clamp(scores[i], Eps, 1 - Eps);
            loss -= labels[i] == 1 ? Math.Log(p) : Math.Log(1 - p);
            brier += (scores[i] - labels[i]) * (scores[i] - labels[i]);
        }

        var order = Enumerable.Range(0, n).OrderBy(i => scores[i]).ToArray();
        return new ProbabilityMetrics(n, positives, loss / n, brier / n, Auc(scores, labels, order, positives), Ece(scores, labels, order, bins), Lift(labels, order, positives));
    }

    private static double? Auc(IReadOnlyList<double> scores, IReadOnlyList<int> labels, int[] order, int positives)
    {
        var n = order.Length;
        var negatives = n - positives;
        if (positives == 0 || negatives == 0)
        {
            return null;
        }

        double rankSum = 0;
        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j + 1 < n && scores[order[j + 1]] == scores[order[i]])
            {
                j++;
            }

            var rank = ((i + 1) + (j + 1)) / 2.0;
            for (var k = i; k <= j; k++)
            {
                if (labels[order[k]] == 1)
                {
                    rankSum += rank;
                }
            }

            i = j + 1;
        }

        return (rankSum - (positives * (positives + 1.0) / 2.0)) / ((double)positives * negatives);
    }

    private static double Ece(IReadOnlyList<double> scores, IReadOnlyList<int> labels, int[] order, int bins)
    {
        var n = order.Length;
        var b = Math.Min(bins, n);
        double total = 0;
        for (var k = 0; k < b; k++)
        {
            int from = (int)((long)k * n / b), to = (int)((long)(k + 1) * n / b);
            if (to == from)
            {
                continue;
            }

            double p = 0, y = 0;
            for (var i = from; i < to; i++)
            {
                p += scores[order[i]];
                y += labels[order[i]];
            }

            var size = to - from;
            total += (size / (double)n) * Math.Abs((p / size) - (y / size));
        }

        return total * 100;
    }

    private static double? Lift(IReadOnlyList<int> labels, int[] order, int positives)
    {
        var n = order.Length;
        if (positives == 0)
        {
            return null;
        }

        var top = Math.Max(1, (int)Math.Ceiling(n * 0.2));
        var wins = 0;
        for (var i = n - top; i < n; i++)
        {
            wins += labels[order[i]];
        }

        return ((double)wins / top) / ((double)positives / n);
    }
}

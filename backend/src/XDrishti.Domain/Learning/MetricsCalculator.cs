namespace XDrishti.Domain.Learning;

/// <summary>Pure metrics of predictions (P6-05). Tied scores are never split: they share a rank (AUC), a calibration bin (ECE) and the top-fifth slots (lift), so the row order does not matter.</summary>
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
        return new ProbabilityMetrics(n, positives, loss / n, brier / n, Auc(scores, labels, order, positives), Ece(scores, labels, order, bins), Lift(scores, labels, order, positives));
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
        // Rows with the same score are one unit, so a bin never cuts through a tie and the input order cannot matter.
        var n = order.Length;
        var b = Math.Min(bins, n);
        double total = 0, p = 0, y = 0;
        long size = 0;
        var closed = 0;
        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j + 1 < n && scores[order[j + 1]] == scores[order[i]])
            {
                j++;
            }

            for (var k = i; k <= j; k++)
            {
                y += labels[order[k]];
            }

            p += scores[order[i]] * (j - i + 1);
            size += j - i + 1;
            i = j + 1;
            if (size * b >= (long)(closed + 1) * n || i == n)
            {
                total += (size / (double)n) * Math.Abs((p / size) - (y / size));
                closed = (int)Math.Min(b, size * b / n);
                p = 0;
                y = 0;
                size = 0;
            }
        }

        return total * 100;
    }

    private static double? Lift(IReadOnlyList<double> scores, IReadOnlyList<int> labels, int[] order, int positives)
    {
        var n = order.Length;
        if (positives == 0)
        {
            return null;
        }

        var top = Math.Max(1, (int)Math.Ceiling(n * 0.2));
        var boundary = scores[order[n - top]];
        double wins = 0;
        int tied = 0, tiedWins = 0, taken = 0;
        for (var i = 0; i < n; i++)
        {
            var row = order[i];
            if (scores[row] == boundary)
            {
                tied++;
                tiedWins += labels[row];
            }
            else if (scores[row] > boundary)
            {
                wins += labels[row];
                taken++;
            }
        }

        // Rows tied at the boundary share the remaining slots equally, so the input order cannot matter.
        wins += (top - taken) * (tiedWins / (double)tied);
        return (wins / top) / ((double)positives / n);
    }
}

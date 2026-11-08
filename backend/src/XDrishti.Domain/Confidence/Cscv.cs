using System.Numerics;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// Combinatorially symmetric cross-validation and the probability of backtest overfitting (Bailey, Borwein, Lopez de Prado and Zhu; CF-03 part B, ADR 0228). Each variant has one series of daily results over the same days. The days are cut into S contiguous blocks; for every
/// choice of S/2 blocks as the in-sample part (the other S/2 are out of sample) the in-sample best variant is found by <see cref="PboMetric"/>, its out-of-sample rank among all variants is taken (ascending: rank 1 is the lowest, rank N the highest; ties take the
/// middle rank; variants tied for the in-sample best share the pick and their out-of-sample ranks are averaged), <c>omega = rank / (N + 1)</c> and <c>lambda = ln(omega / (1 - omega))</c>. PBO is the share of splits with <c>lambda &lt;= 0</c>. Pure and deterministic. The formulas were checked by hand on the spec's case, not against the full paper.
/// </summary>
public static class Cscv
{
    /// <summary>The fewest blocks.</summary>
    public const int MinBlocks = 4;

    /// <summary>The most blocks (<c>C(16, 8) = 12870</c> splits).</summary>
    public const int MaxBlocks = 16;

    /// <summary>A standard deviation at or below this share of (1 + |mean|) is taken as zero when scoring by Sharpe.</summary>
    private const double FlatSd = 1e-12;

    /// <summary>The number of splits for <paramref name="blocks"/> blocks: <c>C(S, S/2)</c>.</summary>
    public static int SplitCount(int blocks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blocks, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blocks, 30);
        var half = blocks / 2;
        long c = 1;
        for (var i = 1; i <= half; i++)
        {
            c = c * (blocks - half + i) / i;
        }

        return checked((int)c);
    }

    /// <summary>The probability of backtest overfitting of <paramref name="seriesByVariant"/> (each variant's daily results, same length and the same days).</summary>
    /// <param name="seriesByVariant">One series per variant, at least 2.</param>
    /// <param name="blocks">S: even, 4 to 16, at most the number of days. When the days do not divide by S the earliest remainder days are left out.</param>
    /// <param name="metric">The score that picks and ranks.</param>
    public static PboResult Pbo(IReadOnlyList<double[]> seriesByVariant, int blocks, PboMetric metric = PboMetric.Mean)
    {
        ArgumentNullException.ThrowIfNull(seriesByVariant);
        var n = seriesByVariant.Count;
        if (blocks is < MinBlocks or > MaxBlocks || blocks % 2 != 0)
        {
            return PboResult.Missing("blocks must be an even number from 4 to 16", n, blocks);
        }

        if (n < 2)
        {
            return PboResult.Missing("needs at least 2 variants", n, blocks);
        }

        var days = seriesByVariant[0].Length;
        if (seriesByVariant.Any(s => s is null || s.Length != days))
        {
            return PboResult.Missing("the variants' series do not cover the same days", n, blocks);
        }

        if (days < blocks)
        {
            return PboResult.Missing("fewer days than blocks", n, blocks);
        }

        if (seriesByVariant.Any(s => s.Any(v => !double.IsFinite(v))))
        {
            return PboResult.Missing("a daily value is not a finite number", n, blocks);
        }

        if (seriesByVariant.Skip(1).All(s => s.AsSpan().SequenceEqual(seriesByVariant[0])))
        {
            return PboResult.Missing("all variants are identical, so the best of them is undefined", n, blocks);
        }

        var size = days / blocks;
        var skip = days - (size * blocks);
        var sum = new double[n, blocks];
        var squares = new double[n, blocks];
        for (var v = 0; v < n; v++)
        {
            for (var b = 0; b < blocks; b++)
            {
                for (var d = 0; d < size; d++)
                {
                    var x = seriesByVariant[v][skip + (b * size) + d];
                    sum[v, b] += x;
                    squares[v, b] += x * x;
                }
            }
        }

        var full = (1 << blocks) - 1;
        var half = blocks / 2;
        var logits = new List<double>(SplitCount(blocks));
        var inSample = new double[n];
        var outSample = new double[n];
        for (var mask = 0; mask <= full; mask++)
        {
            if (BitOperations.PopCount((uint)mask) != half)
            {
                continue;
            }

            for (var v = 0; v < n; v++)
            {
                inSample[v] = Score(v, mask, blocks, size, sum, squares, metric);
                outSample[v] = Score(v, full ^ mask, blocks, size, sum, squares, metric);
            }

            // the in-sample best: variants tied at the top share the pick, and their out-of-sample ranks are averaged (a tie must not always favour the first variant, which is the base)
            var top = inSample[0];
            for (var v = 1; v < n; v++)
            {
                if (inSample[v] > top)
                {
                    top = inSample[v];
                }
            }

            var rankSum = 0.0;
            var tied = 0;
            for (var best = 0; best < n; best++)
            {
                if (inSample[best] != top)
                {
                    continue;
                }

                var below = 0;
                var equal = 0;
                for (var v = 0; v < n; v++)
                {
                    if (v == best)
                    {
                        continue;
                    }

                    if (outSample[v] < outSample[best])
                    {
                        below++;
                    }
                    else if (outSample[v] == outSample[best])
                    {
                        equal++;
                    }
                }

                rankSum += 1 + below + (equal / 2.0);
                tied++;
            }

            var rank = rankSum / tied;
            var omega = rank / (n + 1.0);
            logits.Add(Math.Log(omega / (1 - omega)));
        }

        return FromLogits(logits, n, blocks);
    }

    /// <summary>PBO and the median logit from the logits of the splits: the share at or below 0 (an exact 0 counts, as the paper's <c>lambda &lt;= 0</c>).</summary>
    public static PboResult FromLogits(IReadOnlyList<double> logits, int variants, int blocks)
    {
        ArgumentNullException.ThrowIfNull(logits);
        if (logits.Count == 0 || logits.Any(l => !double.IsFinite(l)))
        {
            return PboResult.Missing("no usable split", variants, blocks);
        }

        var sorted = logits.OrderBy(l => l).ToArray();
        var mid = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        return new PboResult(logits.Count(l => l <= 0) / (double)logits.Count, median, variants, blocks, logits.Count, null);
    }

    private static double Score(int variant, int mask, int blocks, int size, double[,] sum, double[,] squares, PboMetric metric)
    {
        double total = 0;
        double sq = 0;
        var count = 0;
        for (var b = 0; b < blocks; b++)
        {
            if ((mask & (1 << b)) == 0)
            {
                continue;
            }

            total += sum[variant, b];
            sq += squares[variant, b];
            count += size;
        }

        var mean = total / count;
        if (metric == PboMetric.Mean)
        {
            return mean;
        }

        if (count < 2)
        {
            return mean == 0 ? 0 : mean > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        }

        var sd = Math.Sqrt(Math.Max(0, (sq - (total * total / count)) / (count - 1)));
        if (sd <= FlatSd * (1 + Math.Abs(mean)))
        {
            return Math.Abs(mean) <= FlatSd ? 0 : mean > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        }

        return mean / sd;
    }
}

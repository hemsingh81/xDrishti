namespace XDrishti.Domain.Learning;

/// <summary>
/// The logistic-regression baseline (P6-05), written here so that Domain needs no package: features standardised on the train rows (a missing or non-finite value counts as the train mean), a ridge penalty on the weights (not the intercept), Newton steps solved by
/// Gaussian elimination with partial pivoting, at most 50 steps. Sequential sums only, so the same rows give the same weights. With no features, or one class in the train rows, it is the base rate.
/// </summary>
/// <param name="Ridge">The penalty on the squared weights (1).</param>
public sealed class LogisticScorer(double Ridge = 1) : IScorerFactory
{
    /// <summary>The report name.</summary>
    public const string ScorerName = "logistic";

    private const int MaxSteps = 50;
    private const double Tolerance = 1e-8;

    /// <inheritdoc />
    public string Name => ScorerName;

    /// <inheritdoc />
    public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
    {
        ArgumentNullException.ThrowIfNull(train);
        var baseRate = BaseRateScorer.Rate(train);
        var width = train.Count == 0 ? 0 : train[0].Features.Length;
        var wins = train.Count(r => r.Label == 1);
        if (width == 0 || wins == 0 || wins == train.Count)
        {
            return new Model(baseRate, [], [], [], 0);
        }

        var mean = new double[width];
        var sd = new double[width];
        for (var j = 0; j < width; j++)
        {
            var values = train.Select(r => r.Features[j]).Where(double.IsFinite).ToArray();
            mean[j] = values.Length == 0 ? 0 : values.Sum() / values.Length;
            var m = mean[j];
            var variance = values.Length == 0 ? 0 : values.Sum(v => (v - m) * (v - m)) / values.Length;
            sd[j] = variance > 1e-12 ? Math.Sqrt(variance) : 1;
        }

        var x = train.Select(r => Standardise(r, mean, sd)).ToArray();
        var beta = new double[width + 1];
        beta[0] = Math.Log(baseRate / (1 - baseRate));
        for (var step = 0; step < MaxSteps; step++)
        {
            var gradient = new double[width + 1];
            var hessian = new double[width + 1, width + 1];
            for (var i = 0; i < x.Length; i++)
            {
                var p = Sigmoid(Linear(beta, x[i]));
                var w = Math.Max(p * (1 - p), 1e-9);
                var e = p - train[i].Label;
                gradient[0] += e;
                hessian[0, 0] += w;
                for (var a = 0; a < width; a++)
                {
                    gradient[a + 1] += e * x[i][a];
                    hessian[0, a + 1] += w * x[i][a];
                    hessian[a + 1, 0] += w * x[i][a];
                    for (var b = 0; b < width; b++)
                    {
                        hessian[a + 1, b + 1] += w * x[i][a] * x[i][b];
                    }
                }
            }

            for (var a = 0; a < width; a++)
            {
                gradient[a + 1] += Ridge * beta[a + 1];
                hessian[a + 1, a + 1] += Ridge;
            }

            var delta = Solve(hessian, gradient);
            var largest = 0.0;
            for (var a = 0; a <= width; a++)
            {
                beta[a] -= delta[a];
                largest = Math.Max(largest, Math.Abs(delta[a]));
            }

            if (largest < Tolerance)
            {
                break;
            }
        }

        return new Model(baseRate, beta, mean, sd, width);
    }

    private static double[] Standardise(ValidationRow row, double[] mean, double[] sd)
    {
        var z = new double[mean.Length];
        for (var j = 0; j < z.Length; j++)
        {
            var v = j < row.Features.Length ? row.Features[j] : double.NaN;
            z[j] = double.IsFinite(v) ? (v - mean[j]) / sd[j] : 0;
        }

        return z;
    }

    private static double Linear(double[] beta, double[] z)
    {
        var s = beta[0];
        for (var j = 0; j < z.Length; j++)
        {
            s += beta[j + 1] * z[j];
        }

        return s;
    }

    private static double Sigmoid(double t) => t >= 0 ? 1 / (1 + Math.Exp(-t)) : Math.Exp(t) / (1 + Math.Exp(t));

    private static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (pivot != col)
            {
                for (var c = 0; c < n; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }

                (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            var d = Math.Abs(m[col, col]) < 1e-12 ? 1e-12 : m[col, col];
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] / d;
                for (var c = col; c < n; c++)
                {
                    m[r, c] -= f * m[col, c];
                }

                v[r] -= f * v[col];
            }
        }

        var x = new double[n];
        for (var r = n - 1; r >= 0; r--)
        {
            var s = v[r];
            for (var c = r + 1; c < n; c++)
            {
                s -= m[r, c] * x[c];
            }

            x[r] = s / (Math.Abs(m[r, r]) < 1e-12 ? 1e-12 : m[r, r]);
        }

        return x;
    }

    private sealed class Model(double baseRate, double[] beta, double[] mean, double[] sd, int width) : IRowScorer
    {
        public double Score(ValidationRow row) => width == 0 ? baseRate : Sigmoid(Linear(beta, Standardise(row, mean, sd)));
    }
}

using Microsoft.ML.Data;

namespace XDrishti.MlSpike;

/// <summary>
/// One synthetic training example shaped like P6's meta-model input: numeric market features, categorical setup descriptors,
/// a binary label and the known true probability (so calibration and "how close to optimal" can be measured).
/// </summary>
public sealed class Row
{
    public const int NumericCount = 36;

    [VectorType(NumericCount)]
#pragma warning disable CA1819 // ML.NET maps a float[] property to a vector column.
    public float[] Numeric { get; set; } = new float[NumericCount];
#pragma warning restore CA1819

    public string Strategy { get; set; } = string.Empty;

    public string Timeframe { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string Regime { get; set; } = string.Empty;

    public string Weekday { get; set; } = string.Empty;

    public bool Label { get; set; }

    public float TrueProbability { get; set; }
}

/// <summary>Scored output used for single-row prediction after a model load.</summary>
public sealed class Scored
{
    public float Score { get; set; }

    public float Probability { get; set; }

    public bool PredictedLabel { get; set; }
}

/// <summary>Deterministic synthetic data: the label is Bernoulli of a known function of a few features (plus two interactions), ~45% positive.</summary>
public static class Generator
{
    private static readonly string[] Strategies = ["ORB", "VWAP_PULLBACK", "BASE_BREAKOUT", "EMA_TREND", "RSI_REVERT", "GAP_FADE", "RANGE_BREAK", "OTHER"];
    private static readonly string[] Timeframes = ["5m", "15m", "30m", "1h"];
    private static readonly string[] Sides = ["BUY", "SELL"];
    private static readonly string[] Styles = ["intraday", "swing"];
    private static readonly string[] Regimes = ["trend_up", "trend_down", "range", "high_vol", "low_vol"];
    private static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri"];

    /// <summary>Names of the numeric features that truly drive the label (used to sanity-check feature contributions).</summary>
    public static readonly string[] PlantedFeatures = ["Numeric.0", "Numeric.1", "Numeric.2", "Numeric.3", "Numeric.4", "Numeric.5"];

    public static Row[] Generate(int count, int seed)
    {
        var random = new Random(seed);
        var rows = new Row[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = One(random);
        }

        return rows;
    }

    /// <summary>The true P(label = 1) of a row, from the generating function alone.</summary>
    public static double TrueProbabilityOf(float[] x, string strategy, string regime, string side)
    {
        var logit = -0.15
            + (0.9 * x[0]) - (0.7 * x[1]) + (0.55 * x[2] * x[3]) + (0.6 * Math.Sin(1.5 * x[4]))
            + (0.35 * x[5] * (side == "BUY" ? 1 : -1))
            + (strategy == "BASE_BREAKOUT" ? 0.5 : 0) + (strategy == "GAP_FADE" ? -0.4 : 0)
            + (regime == "high_vol" ? -0.45 : 0) + (regime == "trend_up" ? 0.3 : 0);
        return 1.0 / (1.0 + Math.Exp(-logit));
    }

    private static Row One(Random random)
    {
        var row = new Row();
        var common = Gaussian(random); // a shared market factor so that features are correlated like real ones
        for (var k = 0; k < Row.NumericCount; k++)
        {
            row.Numeric[k] = (float)((0.35 * common) + (0.94 * Gaussian(random)));
        }

        row.Strategy = Strategies[random.Next(Strategies.Length)];
        row.Timeframe = Timeframes[random.Next(Timeframes.Length)];
        row.Side = Sides[random.Next(Sides.Length)];
        row.Style = Styles[random.Next(Styles.Length)];
        row.Regime = Regimes[random.Next(Regimes.Length)];
        row.Weekday = Weekdays[random.Next(Weekdays.Length)];
        var p = TrueProbabilityOf(row.Numeric, row.Strategy, row.Regime, row.Side);
        row.TrueProbability = (float)p;
        row.Label = random.NextDouble() < p;
        return row;
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

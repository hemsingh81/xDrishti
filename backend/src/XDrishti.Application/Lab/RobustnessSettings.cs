using System.Globalization;

namespace XDrishti.Application.Lab;

/// <summary>The <c>StrategyLab:Robustness</c> keys (spec P5-05): how many parameters and which steps the sensitivity sweep takes and how many draws the Monte Carlo and the baseline make.</summary>
public sealed record RobustnessSettings(int SweepParams, IReadOnlyList<double> SweepSteps, int McDraws)
{
    /// <summary>Four parameters, 0.8 0.9 1.1 1.2 times the declared value, 1000 draws.</summary>
    public static RobustnessSettings Default { get; } = new(4, [0.8, 0.9, 1.1, 1.2], 1000);

    /// <summary>True when the values are in range.</summary>
    public bool IsValid => SweepParams is >= 1 and <= 4 && SweepSteps.Count is >= 1 and <= 4 && SweepSteps.All(s => double.IsFinite(s) && s > 0) && McDraws is >= 100 and <= 20000;

    /// <summary>The text that stands for these settings in the key of a robust run.</summary>
    public string ConfigText() =>
        "robust.params=" + SweepParams.ToString(CultureInfo.InvariantCulture)
        + ";robust.steps=" + string.Join(",", SweepSteps.Select(s => s.ToString("R", CultureInfo.InvariantCulture)))
        + ";robust.draws=" + McDraws.ToString(CultureInfo.InvariantCulture);
}

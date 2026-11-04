using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence</c> settings of CF-01 (ADR 0203). Defaults are the spec's; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode">How effective n is used; default <see cref="ConfidenceMode.ReportOnly"/>.</param>
/// <param name="MinSessionsForAutocorr">Fewest entry sessions with a trade for the serial estimate (5..250, default 20).</param>
/// <param name="MinTradesForEstimate">Fewer trades than this get no effective n, power or MinTRL (3..50, default 5).</param>
/// <param name="MinClusters">Fewest entry sessions for the cluster estimate (3..50, default 5).</param>
/// <param name="Alpha">One-sided significance level (0.001..0.2, default 0.05).</param>
/// <param name="Power">Power (0.5..0.99, default 0.80).</param>
/// <param name="SigmaFloorR">The floor of the standard deviation of R used for power (0.1..5, default 0.5).</param>
/// <param name="TargetEdgeR">The edge the power statement is about; null takes the gate's minimum expectancy (0.15 by default); otherwise 0.01..5.</param>
/// <param name="Confidence">MinTRL confidence (0.5..0.999, default 0.95).</param>
/// <param name="BenchmarkSr">MinTRL benchmark Sharpe ratio per trade (-1..1, default 0).</param>
public sealed record ConfidenceOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    int MinSessionsForAutocorr = 20,
    int MinTradesForEstimate = 5,
    int MinClusters = 5,
    double Alpha = 0.05,
    double Power = 0.80,
    double SigmaFloorR = 0.5,
    double? TargetEdgeR = null,
    double Confidence = 0.95,
    double BenchmarkSr = 0)
{
    /// <summary>The edge of the power statement: the setting, else <paramref name="gateMinExpectancyR"/>.</summary>
    public double EdgeOr(double gateMinExpectancyR) => TargetEdgeR ?? gateMinExpectancyR;

    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values for the key of a snapshot (every value that changes a stored figure).</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture,
        $"{Mode}/{MinSessionsForAutocorr}/{MinTradesForEstimate}/{MinClusters}/{Alpha:R}/{Power:R}/{SigmaFloorR:R}/{(TargetEdgeR.HasValue ? TargetEdgeR.Value.ToString("R", CultureInfo.InvariantCulture) : "gate")}/{Confidence:R}/{BenchmarkSr:R}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode))
        {
            problems.Add("EffectiveN:Mode must be Off, ReportOnly or Gate");
        }

        if (MinSessionsForAutocorr is < 5 or > 250)
        {
            problems.Add("EffectiveN:MinSessionsForAutocorr must be 5 to 250");
        }

        if (MinTradesForEstimate is < 3 or > 50)
        {
            problems.Add("EffectiveN:MinTradesForEstimate must be 3 to 50");
        }

        if (MinClusters is < 3 or > 50)
        {
            problems.Add("EffectiveN:MinClusters must be 3 to 50");
        }

        if (!double.IsFinite(Alpha) || Alpha is < 0.001 or > 0.2)
        {
            problems.Add("Power:Alpha must be 0.001 to 0.2");
        }

        if (!double.IsFinite(Power) || Power is < 0.5 or > 0.99)
        {
            problems.Add("Power:Power must be 0.5 to 0.99");
        }

        if (!double.IsFinite(SigmaFloorR) || SigmaFloorR is < 0.1 or > 5)
        {
            problems.Add("Power:SigmaFloorR must be 0.1 to 5");
        }

        if (TargetEdgeR is { } edge && (!double.IsFinite(edge) || edge is < 0.01 or > 5))
        {
            problems.Add("Power:TargetEdgeR must be 0.01 to 5");
        }

        if (!double.IsFinite(Confidence) || Confidence is < 0.5 or > 0.999)
        {
            problems.Add("MinTrl:Confidence must be 0.5 to 0.999");
        }

        if (!double.IsFinite(BenchmarkSr) || BenchmarkSr is < -1 or > 1)
        {
            problems.Add("MinTrl:BenchmarkSr must be -1 to 1");
        }

        return problems.AsReadOnly();
    }
}

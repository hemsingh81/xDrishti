using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Shrinkage</c> settings of CF-02 (ADR 0204). Defaults are the spec's; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing), <see cref="ConfidenceMode.ReportOnly"/> (figures stored and shown beside the raw ones; the default). <see cref="ConfidenceMode.Gate"/> is not offered yet (see <see cref="Problems"/>).</param>
/// <param name="TauSquaredFloor">The least between-cell variance of mean R used, in R squared (0.0001..1, default 0.01; above 0 so the lower bound exists).</param>
/// <param name="MinTradesForSibling">A cell with fewer trades is not a sibling in the estimates (3..30, default 5).</param>
/// <param name="MinParentTrades">A parent group needs this many trades, the cell excluded (10..200, default 30).</param>
/// <param name="StrengthMin">The least estimated Beta strength (1..1000, default 5).</param>
/// <param name="StrengthMax">The greatest estimated Beta strength (1..1000, default 200).</param>
/// <param name="QuantileLevel">The lower bound is this quantile of the posterior (0.01..0.25, default 0.05).</param>
/// <param name="SigmaPoolStrength">A cell's variance is pooled with the group's with weight n over n plus this (1..1000, default 30).</param>
public sealed record ShrinkageOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    double TauSquaredFloor = 0.01,
    int MinTradesForSibling = 5,
    int MinParentTrades = 30,
    double StrengthMin = 5,
    double StrengthMax = 200,
    double QuantileLevel = 0.05,
    double SigmaPoolStrength = 30)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values for the key of a snapshot (every value that changes a stored figure).</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture,
        $"{Mode}/{TauSquaredFloor:R}/{MinTradesForSibling}/{MinParentTrades}/{StrengthMin:R}/{StrengthMax:R}/{QuantileLevel:R}/{SigmaPoolStrength:R}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode))
        {
            problems.Add("Shrinkage:Mode must be Off or ReportOnly");
        }
        else if (Mode == ConfidenceMode.Gate)
        {
            // CF-02 review fix 1: the lower bound ignores the uncertainty of the pooling parameters; it must not decide anything until a seeded coverage simulation has validated it
            problems.Add("Shrinkage:Mode Gate is not offered until the coverage of the lower bound has been validated");
        }

        if (!double.IsFinite(TauSquaredFloor) || TauSquaredFloor is < 0.0001 or > 1)
        {
            problems.Add("Shrinkage:TauSquaredFloor must be 0.0001 to 1");
        }

        if (MinTradesForSibling is < 3 or > 30)
        {
            problems.Add("Shrinkage:MinTradesForSibling must be 3 to 30");
        }

        if (MinParentTrades is < 10 or > 200)
        {
            problems.Add("Shrinkage:MinParentTrades must be 10 to 200");
        }

        if (!double.IsFinite(StrengthMin) || StrengthMin is < 1 or > 1000)
        {
            problems.Add("Shrinkage:StrengthMin must be 1 to 1000");
        }

        if (!double.IsFinite(StrengthMax) || StrengthMax is < 1 or > 1000 || StrengthMax < StrengthMin)
        {
            problems.Add("Shrinkage:StrengthMax must be 1 to 1000 and not below StrengthMin");
        }

        if (!double.IsFinite(QuantileLevel) || QuantileLevel is < 0.01 or > 0.25)
        {
            problems.Add("Shrinkage:QuantileLevel must be 0.01 to 0.25");
        }

        if (!double.IsFinite(SigmaPoolStrength) || SigmaPoolStrength is < 1 or > 1000)
        {
            problems.Add("Shrinkage:SigmaPoolStrength must be 1 to 1000");
        }

        return problems.AsReadOnly();
    }
}

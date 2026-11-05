namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Calibration</c> settings of CF-10 part 10a (ADR 0214): the cut, in calibration rows, at and above which a model's raw score is calibrated by an isotonic map instead of Platt scaling. A proposal of the spec (D1), a setting and not a constant.
/// It has no mode and no enforce switch: it picks the smaller fit for few rows and changes no decision, because a model's probability stays advisory. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="IsotonicMinRows">Calibration rows at and above which the isotonic map is used (default 500); below, Platt scaling.</param>
public sealed record CalibrationOptions(int IsotonicMinRows = CalibrationOptions.DefaultIsotonicMinRows)
{
    /// <summary>The default cut, in calibration rows (the spec's proposal).</summary>
    public const int DefaultIsotonicMinRows = 500;

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (IsotonicMinRows is < 1 or > 100000)
        {
            problems.Add("Calibration:IsotonicMinRows must be 1 to 100000");
        }

        return problems.AsReadOnly();
    }
}

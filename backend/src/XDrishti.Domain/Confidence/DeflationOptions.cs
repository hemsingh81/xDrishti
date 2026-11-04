using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Deflation</c> settings of CF-03 (ADR 0205). Defaults are the spec's; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (no figure) or <see cref="ConfidenceMode.ReportOnly"/> (the default). <see cref="ConfidenceMode.Gate"/> is not offered (see <see cref="Problems"/>).</param>
/// <param name="MinProbability">The probability the figure is read against, shown beside it (0.5..0.999, default 0.95); it decides nothing while the mode is ReportOnly.</param>
/// <param name="AssumedTrialSrSd">The owner's assumption for the standard deviation of the trial Sharpe ratios per trade, used when the stored trial statistics do not cover the family (0.001..1); null (the default) means not assumed.</param>
/// <param name="TBasis">T is the effective trades (default) or the raw trade count.</param>
public sealed record DeflationOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    double MinProbability = 0.95,
    double? AssumedTrialSrSd = null,
    DeflationBasis TBasis = DeflationBasis.NEff)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values, kept with the figure so a stored document says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture,
        $"{Mode}/{MinProbability:R}/{(AssumedTrialSrSd.HasValue ? AssumedTrialSrSd.Value.ToString("R", CultureInfo.InvariantCulture) : "stored")}/{TBasis}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode))
        {
            problems.Add("Deflation:Mode must be Off or ReportOnly");
        }
        else if (Mode == ConfidenceMode.Gate)
        {
            // CF-03: the seventh gate is not built; the null-validation result is recorded in ADR 0205 and Gate is the owner's decision after it
            problems.Add("Deflation:Mode Gate is not offered: the seventh gate is not built");
        }

        if (!double.IsFinite(MinProbability) || MinProbability is < 0.5 or > 0.999)
        {
            problems.Add("Deflation:MinProbability must be 0.5 to 0.999");
        }

        if (AssumedTrialSrSd is { } sd && (!double.IsFinite(sd) || sd is < 0.001 or > 1))
        {
            problems.Add("Deflation:AssumedTrialSrSd must be 0.001 to 1");
        }

        if (!Enum.IsDefined(TBasis))
        {
            problems.Add("Deflation:TBasis must be NEff or Raw");
        }

        return problems.AsReadOnly();
    }
}

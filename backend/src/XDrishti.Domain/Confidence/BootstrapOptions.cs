using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Bootstrap</c> settings of CF-03 (ADR 0205): the stationary block bootstrap of the Lab. Defaults are the spec's; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (no figure) or <see cref="ConfidenceMode.ReportOnly"/> (the default). <see cref="ConfidenceMode.Gate"/> is not offered: the interval decides nothing.</param>
/// <param name="Draws">Resamples (100..20000); null takes <c>Robustness:McDraws</c> so the two intervals are drawn alike.</param>
/// <param name="BlockLength">The mean block length in sessions (2..60); 0 is automatic: <c>round(k^(1/3))</c>, at least 2.</param>
public sealed record BootstrapOptions(ConfidenceMode Mode = ConfidenceMode.ReportOnly, int? Draws = null, int BlockLength = 0)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values (with the draws actually used), kept with the figure.</summary>
    public string KeyText(int drawsUsed) => string.Create(CultureInfo.InvariantCulture, $"{Mode}/{drawsUsed}/{(BlockLength == 0 ? "auto" : BlockLength.ToString(CultureInfo.InvariantCulture))}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("Bootstrap:Mode must be Off or ReportOnly");
        }

        if (Draws is < 100 or > 20000)
        {
            problems.Add("Bootstrap:Draws must be 100 to 20000");
        }

        if (BlockLength != 0 && BlockLength is < 2 or > 60)
        {
            problems.Add("Bootstrap:BlockLength must be 0 (auto) or 2 to 60");
        }

        return problems.AsReadOnly();
    }
}

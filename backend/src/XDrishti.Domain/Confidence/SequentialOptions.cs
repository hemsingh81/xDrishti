using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Sequential</c> settings of CF-09 (ADR 0213): the sequential failure test of the paper edge series. Every number is a proposal of the spec (D1) and a setting, not a constant. The test only flags: it has no mode and no enforce switch,
/// because nothing here can act (the action taken on a flag is CF-12's). <see cref="Problems"/> lists every value out of its range (the host refuses to start on one). The boundaries are computed from the settings, never stored.
/// </summary>
/// <param name="Mu1">The alternative mean net R per independent trade (default +0.2 R); the indifference point is half of it.</param>
/// <param name="Sigma">The standard deviation of one trade's R (default 1.5 R, P10-00 D7); a floor, never lowered.</param>
/// <param name="Alpha">The false-alarm level of the on-track label (default 0.05).</param>
/// <param name="BetaCaution">The level of the caution boundary (default 0.20).</param>
/// <param name="BetaFail">The level of the failure boundary (default 0.05).</param>
/// <param name="SigmaFromN">From how many earlier trades the sample standard deviation may raise sigma (default 20).</param>
public sealed record SequentialOptions(double Mu1 = 0.2, double Sigma = 1.5, double Alpha = 0.05, double BetaCaution = 0.20, double BetaFail = 0.05, int SigmaFromN = 20)
{
    /// <summary>The caution boundary, <c>ln(beta_caution / (1 - alpha))</c> (-1.558145 with the defaults).</summary>
    public double CautionBoundary => Math.Log(BetaCaution / (1 - Alpha));

    /// <summary>The failure boundary, <c>ln(beta_fail / (1 - alpha))</c> (-2.944439 with the defaults).</summary>
    public double FailureBoundary => Math.Log(BetaFail / (1 - Alpha));

    /// <summary>The on-track level, <c>ln((1 - beta_caution) / alpha)</c> (2.772589 with the defaults); the running sum is reflected here. A label, never a pass.</summary>
    public double OnTrackLevel => Math.Log((1 - BetaCaution) / Alpha);

    /// <summary>The evidence ratio at the caution boundary, <c>(1 - alpha) / beta_caution</c> (4.75).</summary>
    public double CautionOdds => (1 - Alpha) / BetaCaution;

    /// <summary>The evidence ratio at the failure boundary, <c>(1 - alpha) / beta_fail</c> (19).</summary>
    public double FailureOdds => (1 - Alpha) / BetaFail;

    /// <summary>The text of the values, kept with every figure so a response says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"mu1={Mu1:R}/sigma={Sigma:R}/alpha={Alpha:R}/betaCaution={BetaCaution:R}/betaFail={BetaFail:R}/sigmaFromN={SigmaFromN}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(Mu1) || Mu1 is <= 0 or > 2)
        {
            problems.Add("Sequential:Mu1 must be above 0 and at most 2 (R per trade)");
        }

        if (!double.IsFinite(Sigma) || Sigma is < 0.5 or > 5)
        {
            problems.Add("Sequential:Sigma must be 0.5 to 5 (R)");
        }

        if (!double.IsFinite(Alpha) || Alpha is <= 0 or > 0.2)
        {
            problems.Add("Sequential:Alpha must be above 0 and at most 0.2");
        }

        if (!double.IsFinite(BetaCaution) || BetaCaution is <= 0 or > 0.5 || (double.IsFinite(Alpha) && Alpha > 0 && (1 - BetaCaution) / Alpha <= 1))
        {
            problems.Add("Sequential:BetaCaution must be above 0 and at most 0.5, and (1 - BetaCaution) / Alpha must be above 1");
        }

        if (!double.IsFinite(BetaFail) || BetaFail <= 0 || BetaFail >= BetaCaution)
        {
            problems.Add("Sequential:BetaFail must be above 0 and below BetaCaution");
        }

        if (SigmaFromN is < 5 or > 200)
        {
            problems.Add("Sequential:SigmaFromN must be 5 to 200");
        }

        return problems.AsReadOnly();
    }
}

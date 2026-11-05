using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Decay</c> settings of CF-12 part 12a (ADR 0218, spec D1): the decay monitor of a strategy's forward series. Every number is a proposal of the spec. They sit in configuration, not in the versioned home of ADR 0002 (a stored, owner-edited
/// version is a later part). Until that exists configuration can only **tighten** them: a longer window or a wider band would only make an alert rarer, so the window is capped at the spec's 60, the band at 1.5 standard errors and the quarter floor at the spec's
/// -0.05 R. Nothing here acts: the monitor only reports. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="WindowTrades">Trades in one non-overlapping window (default 60, the spec's unit; 20 to 60).</param>
/// <param name="Sigmas">How many standard errors below the reference mean a window mean must be to alert (default 1.5; above 0 up to 1.5).</param>
/// <param name="QuarterFloorR">A calendar quarter whose mean net R is below this counts as a weak quarter (default -0.05 R; -0.05 to 0).</param>
public sealed record DecayOptions(int WindowTrades = DecayOptions.DefaultWindowTrades, double Sigmas = DecayOptions.DefaultSigmas, double QuarterFloorR = DecayOptions.DefaultQuarterFloorR)
{
    /// <summary>The spec's proposal, which is also the longest window configuration may take.</summary>
    public const int DefaultWindowTrades = 60;

    /// <summary>The spec's proposal, which is also the widest band (in standard errors) configuration may take.</summary>
    public const double DefaultSigmas = 1.5;

    /// <summary>The spec's proposal, which is also the lowest floor configuration may take.</summary>
    public const double DefaultQuarterFloorR = -0.05;

    /// <summary>The text of the values, kept with every figure so a response says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"window={WindowTrades}/sigmas={Sigmas:R}/quarterFloorR={QuarterFloorR:R}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (WindowTrades is < 20 or > DefaultWindowTrades)
        {
            problems.Add("Decay:WindowTrades must be 20 to 60 (it can only be tightened)");
        }

        if (!double.IsFinite(Sigmas) || Sigmas is <= 0 or > DefaultSigmas)
        {
            problems.Add("Decay:Sigmas must be above 0 up to 1.5 (it can only be tightened)");
        }

        if (!double.IsFinite(QuarterFloorR) || QuarterFloorR is < DefaultQuarterFloorR or > 0)
        {
            problems.Add("Decay:QuarterFloorR must be -0.05 to 0 (it can only be tightened)");
        }

        return problems.AsReadOnly();
    }
}

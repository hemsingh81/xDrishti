using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Loop</c> settings of CF-12 part 12a (ADR 0218, spec D4 to D6): "is the learning loop helping?". Every number is a proposal of the spec and sits in configuration, not in the versioned home of ADR 0002. Where a looser value would weaken
/// a bar the value has the spec's proposal as its floor or ceiling and configuration can only tighten it: at least 20 distinct days, at least 2000 draws, a "helps" bar of at least 0.95, a reliability window of at least 200 outcomes (never an alarm below 200), the
/// alarm band no wider than 0.8 to 1.2 and the standard-error test no wider than 2. Nothing here acts: the loop report only describes. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="MinDays">The fewest distinct trade days for an arm comparison (default 20; 20 to 250: a trade-off, since more days delay every verdict). Fewer is "inconclusive".</param>
/// <param name="Draws">Resamples of the day bootstrap (default 2000; 2000 to 20000).</param>
/// <param name="Level">Coverage of the two-sided interval (default 0.90; 0.90 to 0.99).</param>
/// <param name="HelpsAt">P(first arm better) at or above this is "helps" (default 0.95; 0.95 to 0.999). "Hurts" has its own fixed bar, <see cref="HurtsBar"/>.</param>
/// <param name="MinSlopePer50Rows">The learning curve is "flat" when even its upper slope bound is below this gain per 50 rows (default 0.002; above 0 up to 0.1).</param>
/// <param name="TargetGain">The out-of-time gain the projection aims at (default 0.01; 0.001 to 0.1).</param>
/// <param name="SlopeLow">The slope alarms below this (default 0.8; 0.8 up to but not including 1).</param>
/// <param name="SlopeHigh">The slope alarms above this (default 1.2; above 1 up to 1.2).</param>
/// <param name="SlopeSe">...and the slope must also be this many standard errors from 1 (default 2; 1 to 2).</param>
public sealed record LoopOptions(
    int MinDays = LoopOptions.DefaultMinDays,
    int Draws = LoopOptions.DefaultDraws,
    double Level = LoopOptions.DefaultLevel,
    double HelpsAt = LoopOptions.DefaultHelpsAt,
    double MinSlopePer50Rows = 0.002,
    double TargetGain = 0.01,
    double SlopeLow = LoopOptions.DefaultSlopeLow,
    double SlopeHigh = LoopOptions.DefaultSlopeHigh,
    double SlopeSe = LoopOptions.DefaultSlopeSe)
{
    /// <summary>The spec's proposals, which are also the loosest values configuration may take.</summary>
    public const int DefaultMinDays = 20;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const int DefaultDraws = 2000;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const double DefaultLevel = 0.90;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const double DefaultHelpsAt = 0.95;

    /// <summary>Outcomes in the reliability slope's trailing window, fixed at 200: with fewer the slope is shown and never alarms (not a setting).</summary>
    public const int SlopeWindow = 200;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const double DefaultSlopeLow = 0.8;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const double DefaultSlopeHigh = 1.2;

    /// <inheritdoc cref="DefaultMinDays"/>
    public const double DefaultSlopeSe = 2;

    /// <summary>The fixed bar of "hurts": P(first arm worse) at or above this (0.95, the mirror of the spec's 0.05).</summary>
    public const double HurtsBar = 0.95;

    /// <summary>The "hurts" level in the spec's terms, <c>1 - <see cref="HurtsBar"/></c> (0.05).</summary>
    public const double HurtsAt = 0.05;

    /// <summary>The text of the values, kept with every figure so a response says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"minDays={MinDays}/draws={Draws}/level={Level:R}/helpsAt={HelpsAt:R}/minSlope50={MinSlopePer50Rows:R}/targetGain={TargetGain:R}/slopeBand={SlopeLow:R}..{SlopeHigh:R}/slopeSe={SlopeSe:R}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (MinDays is < DefaultMinDays or > 250)
        {
            problems.Add("Loop:MinDays must be 20 to 250");
        }

        if (Draws is < DefaultDraws or > 20000)
        {
            problems.Add("Loop:Draws must be 2000 to 20000 (it can only be tightened)");
        }

        if (!double.IsFinite(Level) || Level is < DefaultLevel or > 0.99)
        {
            problems.Add("Loop:Level must be 0.90 to 0.99");
        }

        if (!double.IsFinite(HelpsAt) || HelpsAt is < DefaultHelpsAt or > 0.999)
        {
            problems.Add("Loop:HelpsAt must be 0.95 to 0.999 (it can only be tightened)");
        }

        if (!double.IsFinite(MinSlopePer50Rows) || MinSlopePer50Rows is <= 0 or > 0.1)
        {
            problems.Add("Loop:MinSlopePer50Rows must be above 0 up to 0.1");
        }

        if (!double.IsFinite(TargetGain) || TargetGain is < 0.001 or > 0.1)
        {
            problems.Add("Loop:TargetGain must be 0.001 to 0.1");
        }

        if (!double.IsFinite(SlopeLow) || SlopeLow is < DefaultSlopeLow or >= 1)
        {
            problems.Add("Loop:SlopeLow must be 0.8 up to but not including 1 (it can only be tightened)");
        }

        if (!double.IsFinite(SlopeHigh) || SlopeHigh is <= 1 or > DefaultSlopeHigh)
        {
            problems.Add("Loop:SlopeHigh must be above 1 up to 1.2 (it can only be tightened)");
        }

        if (!double.IsFinite(SlopeSe) || SlopeSe is < 1 or > DefaultSlopeSe)
        {
            problems.Add("Loop:SlopeSe must be 1 to 2 (it can only be tightened)");
        }

        return problems.AsReadOnly();
    }
}

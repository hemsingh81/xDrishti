using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:MlInfluence</c> thresholds of CF-10 part 10c-2 (ADR 0217, spec D6): what a model must show before the predicate <c>MlMayInfluence</c> can hold. Every number is a proposal of the spec. They only set how much evidence is asked for, and until the owner-act settings exist (10d) configuration can only **tighten** them: each value has the spec's proposal as its floor (or ceiling for the calibration error); the owner's switch is not here (it is a stored
/// owner act, a later slice) and no value here can make a model influence a decision: an unavailable figure never counts as a pass whatever these say. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="HoldOutMinRows">Rows the model must have been scored on in the locked hold-out (default 100).</param>
/// <param name="HoldOutMinProbability">The probability, from the paired bootstrap, that the model beats the best no-ML baseline there (default 0.95, the pass bar of spec D4).</param>
/// <param name="ForwardMinCandidates">Graded shadow candidates scored by the model (default 30).</param>
/// <param name="ForwardMinWeeks">Weeks the forward evidence must span (default 4).</param>
/// <param name="ForwardMaxEcePp">The largest rolling expected calibration error, in percentage points (default 5).</param>
public sealed record MlInfluenceOptions(
    int HoldOutMinRows = MlInfluenceOptions.DefaultHoldOutMinRows,
    double HoldOutMinProbability = MlInfluenceOptions.DefaultHoldOutMinProbability,
    int ForwardMinCandidates = MlInfluenceOptions.DefaultForwardMinCandidates,
    int ForwardMinWeeks = MlInfluenceOptions.DefaultForwardMinWeeks,
    double ForwardMaxEcePp = MlInfluenceOptions.DefaultForwardMaxEcePp)
{
    /// <summary>The spec's proposals (D6), which are also the loosest values configuration may take.</summary>
    public const int DefaultHoldOutMinRows = 100;

    /// <inheritdoc cref="DefaultHoldOutMinRows"/>
    public const double DefaultHoldOutMinProbability = 0.95;

    /// <inheritdoc cref="DefaultHoldOutMinRows"/>
    public const int DefaultForwardMinCandidates = 30;

    /// <inheritdoc cref="DefaultHoldOutMinRows"/>
    public const int DefaultForwardMinWeeks = 4;

    /// <inheritdoc cref="DefaultHoldOutMinRows"/>
    public const double DefaultForwardMaxEcePp = 5;

    /// <summary>The text of the values, for a stored check that must say which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"holdOutRows={HoldOutMinRows}/holdOutP={HoldOutMinProbability:R}/forwardCandidates={ForwardMinCandidates}/forwardWeeks={ForwardMinWeeks}/forwardEcePp={ForwardMaxEcePp:R}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (HoldOutMinRows is < DefaultHoldOutMinRows or > 100000)
        {
            problems.Add("MlInfluence:HoldOutMinRows must be 100 to 100000 (it can only be tightened)");
        }

        if (!double.IsFinite(HoldOutMinProbability) || HoldOutMinProbability is < DefaultHoldOutMinProbability or > 0.999)
        {
            problems.Add("MlInfluence:HoldOutMinProbability must be 0.95 to 0.999 (it can only be tightened)");
        }

        if (ForwardMinCandidates is < DefaultForwardMinCandidates or > 100000)
        {
            problems.Add("MlInfluence:ForwardMinCandidates must be 30 to 100000 (it can only be tightened)");
        }

        if (ForwardMinWeeks is < DefaultForwardMinWeeks or > 520)
        {
            problems.Add("MlInfluence:ForwardMinWeeks must be 4 to 520 (it can only be tightened)");
        }

        if (!double.IsFinite(ForwardMaxEcePp) || ForwardMaxEcePp is <= 0 or > DefaultForwardMaxEcePp)
        {
            problems.Add("MlInfluence:ForwardMaxEcePp must be above 0 up to 5 (it can only be tightened)");
        }

        return problems.AsReadOnly();
    }
}

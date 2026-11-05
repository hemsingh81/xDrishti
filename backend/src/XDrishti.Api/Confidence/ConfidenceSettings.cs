using XDrishti.Domain.Confidence;

namespace XDrishti.Api.Confidence;

/// <summary>
/// The <c>Confidence</c> configuration section (CF-01, ADR 0203): the settings of effective n, power and minimum track record. A key left out keeps the default; a value out of range stops the host at start (<see cref="ConfidenceOptions.Problems"/>).
/// Changing <c>EffectiveN:Mode</c> to <c>Gate</c> is the owner's act and changes the key of every later cell snapshot, so it is recorded with the snapshot.
/// </summary>
internal sealed class ConfidenceSettings
{
    /// <summary>The section name.</summary>
    public const string SectionName = "Confidence";

    public EffectiveNSection EffectiveN { get; set; } = new();

    public PowerSection Power { get; set; } = new();

    public MinTrlSection MinTrl { get; set; } = new();

    public ShrinkageSection Shrinkage { get; set; } = new();

    public DeflationSection Deflation { get; set; } = new();

    public BootstrapSection Bootstrap { get; set; } = new();

    public CostStressSection CostStress { get; set; } = new();

    public NullsSection Nulls { get; set; } = new();

    public RegistrationSection Registration { get; set; } = new();

    public HoldOutSection HoldOut { get; set; } = new();

    /// <summary>The <c>Confidence:Registration</c> subsection as the options of CF-06 (ADR 0207).</summary>
    internal RegistrationOptions ToRegistrationOptions() => new(Registration.Enforce, Registration.CountInFactor);

    /// <summary>The <c>Confidence:HoldOut</c> subsection as the options of CF-06 (ADR 0207).</summary>
    internal HoldOutOptions ToHoldOutOptions() => new(HoldOut.Enforce, HoldOut.AutoUnlock, HoldOut.MinMonths, HoldOut.SharePercent, HoldOut.MinTrades, HoldOut.Retention);

    /// <summary>The <c>Confidence:Nulls</c> subsection as the options of CF-07 (ADR 0208).</summary>
    internal NullOptions ToNullOptions() => new(Nulls.Mode, Nulls.Enforce, Nulls.Draws, Nulls.Percentile, Nulls.Redraws);

    /// <summary>The <c>Confidence:CostStress</c> subsection as the options of CF-05 (ADR 0206).</summary>
    internal CostStressOptions ToCostStressOptions() => new(CostStress.Mode, CostStress.Enforce, CostStress.MidMultiplier, CostStress.MidFloorR, CostStress.HighMultiplier, CostStress.HighFloorR, CostStress.DelayMinutes, CostStress.DelayRetention);

    /// <summary>The <c>Confidence:Deflation</c> subsection as the options of CF-03 (ADR 0205).</summary>
    internal DeflationOptions ToDeflationOptions() => new(Deflation.Mode, Deflation.MinProbability, Deflation.AssumedTrialSrSd, Deflation.TBasis);

    /// <summary>The <c>Confidence:Bootstrap</c> subsection as the options of CF-03 (ADR 0205).</summary>
    internal BootstrapOptions ToBootstrapOptions() => new(Bootstrap.Mode, Bootstrap.Draws, Bootstrap.BlockLength);

    /// <summary>The <c>Confidence:Shrinkage</c> subsection as the options of CF-02 (ADR 0204).</summary>
    internal ShrinkageOptions ToShrinkageOptions() => new(
        Shrinkage.Mode,
        Shrinkage.TauSquaredFloor,
        Shrinkage.MinTradesForSibling,
        Shrinkage.MinParentTrades,
        Shrinkage.StrengthMin,
        Shrinkage.StrengthMax,
        Shrinkage.QuantileLevel,
        Shrinkage.SigmaPoolStrength);

    internal ConfidenceOptions ToOptions() => new(
        EffectiveN.Mode,
        EffectiveN.MinSessionsForAutocorr,
        EffectiveN.MinTradesForEstimate,
        EffectiveN.MinClusters,
        Power.Alpha,
        Power.Power,
        Power.SigmaFloorR,
        Power.TargetEdgeR,
        MinTrl.Confidence,
        MinTrl.BenchmarkSr);

    internal sealed class EffectiveNSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public int MinSessionsForAutocorr { get; set; } = 20;

        public int MinTradesForEstimate { get; set; } = 5;

        public int MinClusters { get; set; } = 5;
    }

    internal sealed class PowerSection
    {
        public double Alpha { get; set; } = 0.05;

        public double Power { get; set; } = 0.80;

        public double SigmaFloorR { get; set; } = 0.5;

        public double? TargetEdgeR { get; set; }
    }

    internal sealed class MinTrlSection
    {
        public double Confidence { get; set; } = 0.95;

        public double BenchmarkSr { get; set; }
    }

    internal sealed class ShrinkageSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public double TauSquaredFloor { get; set; } = 0.01;

        public int MinTradesForSibling { get; set; } = 5;

        public int MinParentTrades { get; set; } = 30;

        public double StrengthMin { get; set; } = 5;

        public double StrengthMax { get; set; } = 200;

        public double QuantileLevel { get; set; } = 0.05;

        public double SigmaPoolStrength { get; set; } = 30;
    }

    internal sealed class DeflationSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public double MinProbability { get; set; } = 0.95;

        public double? AssumedTrialSrSd { get; set; }

        public DeflationBasis TBasis { get; set; } = DeflationBasis.NEff;
    }

    internal sealed class BootstrapSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public int? Draws { get; set; }

        public int BlockLength { get; set; }
    }

    internal sealed class CostStressSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public bool Enforce { get; set; }

        public double MidMultiplier { get; set; } = 1.5;

        public double MidFloorR { get; set; } = 0.08;

        public double HighMultiplier { get; set; } = 2;

        public double HighFloorR { get; set; }

        public int DelayMinutes { get; set; } = 1;

        public double DelayRetention { get; set; } = 0.70;
    }

    internal sealed class NullsSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public bool Enforce { get; set; }

        public int Draws { get; set; } = 1000;

        public double Percentile { get; set; } = 95;

        public int Redraws { get; set; } = 5;
    }

    internal sealed class RegistrationSection
    {
        public bool Enforce { get; set; }

        public bool CountInFactor { get; set; }
    }

    internal sealed class HoldOutSection
    {
        public bool Enforce { get; set; }

        public bool AutoUnlock { get; set; }

        public int MinMonths { get; set; } = 6;

        public double SharePercent { get; set; } = 20;

        public int MinTrades { get; set; } = 30;

        public double Retention { get; set; } = 0.5;
    }
}

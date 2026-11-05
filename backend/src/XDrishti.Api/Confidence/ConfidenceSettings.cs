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

    public PlateauSection Plateau { get; set; } = new();

    public SequentialSection Sequential { get; set; } = new();

    public CalibrationSection Calibration { get; set; } = new();

    public LadderSection Ladder { get; set; } = new();

    public PairedBootstrapSection PairedBootstrap { get; set; } = new();

    public MlInfluenceSection MlInfluence { get; set; } = new();

    public RegistrationSection Registration { get; set; } = new();

    public HoldOutSection HoldOut { get; set; } = new();

    /// <summary>The <c>Confidence:Registration</c> subsection as the options of CF-06 (ADR 0207).</summary>
    internal RegistrationOptions ToRegistrationOptions() => new(Registration.Enforce, Registration.CountInFactor);

    /// <summary>The <c>Confidence:HoldOut</c> subsection as the options of CF-06 (ADR 0207).</summary>
    internal HoldOutOptions ToHoldOutOptions() => new(HoldOut.Enforce, HoldOut.AutoUnlock, HoldOut.MinMonths, HoldOut.SharePercent, HoldOut.MinTrades, HoldOut.Retention);

    /// <summary>The <c>Confidence:Nulls</c> subsection as the options of CF-07 (ADR 0208).</summary>
    internal NullOptions ToNullOptions() => new(Nulls.Mode, Nulls.Enforce, Nulls.Draws, Nulls.Percentile, Nulls.Redraws, Nulls.Placebos, Nulls.MinPlacebos);

    /// <summary>The <c>Confidence:Plateau</c> subsection as the options of CF-07 part C (ADR 0210).</summary>
    internal PlateauOptions ToPlateauOptions() => new(Plateau.Mode, Plateau.Enforce, Plateau.Retention, Plateau.MinPoints);

    /// <summary>The <c>Confidence:Sequential</c> subsection as the options of CF-09 (ADR 0213).</summary>
    internal SequentialOptions ToSequentialOptions() => new(Sequential.Mu1, Sequential.Sigma, Sequential.Alpha, Sequential.BetaCaution, Sequential.BetaFail, Sequential.SigmaFromN);

    /// <summary>The <c>Confidence:Calibration</c> subsection as the options of CF-10 part 10a (ADR 0214).</summary>
    internal CalibrationOptions ToCalibrationOptions() => new(Calibration.IsotonicMinRows);

    /// <summary>The <c>Confidence:Ladder</c> subsection as the options of CF-10 part 10b (ADR 0215).</summary>
    internal LadderOptions ToLadderOptions() => new(Ladder.Mode, Ladder.Epv, Ladder.MaxLogisticFeatures, Ladder.MinTreeEvents, Ladder.MinProbability, Ladder.PsiWatch, Ladder.PsiAlarm, Ladder.PsiBins, Ladder.MinRows, Ladder.MaxFlipShare);

    /// <summary>The <c>Confidence:PairedBootstrap</c> subsection as the options of CF-10 part 10c-1 (ADR 0216).</summary>
    internal PairedBootstrapOptions ToPairedBootstrapOptions() => new(PairedBootstrap.Mode, PairedBootstrap.Draws, PairedBootstrap.Level, PairedBootstrap.MinDays, PairedBootstrap.PromisingFrom);

    /// <summary>The <c>Confidence:MlInfluence</c> subsection as the options of CF-10 part 10c-2 (ADR 0217).</summary>
    internal MlInfluenceOptions ToMlInfluenceOptions() => new(MlInfluence.HoldOutMinRows, MlInfluence.HoldOutMinProbability, MlInfluence.ForwardMinCandidates, MlInfluence.ForwardMinWeeks, MlInfluence.ForwardMaxEcePp);

    /// <summary>Whether the plateau gate is enforced while the block it lives in (<c>Confidence:Nulls</c>) is computed at all: it is not computed with <c>Nulls:Mode</c> Off, so enforcing it then would cap nothing (ADR 0210).</summary>
    internal bool PlateauEnforcedWithoutNulls() => Plateau.Enforce && Nulls.Mode == ConfidenceMode.Off;

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

        public bool Placebos { get; set; } = true;

        public int MinPlacebos { get; set; } = 19;
    }

    internal sealed class PlateauSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public bool Enforce { get; set; }

        public double Retention { get; set; } = 0.70;

        public int MinPoints { get; set; } = 3;
    }

    internal sealed class SequentialSection
    {
        public double Mu1 { get; set; } = 0.2;

        public double Sigma { get; set; } = 1.5;

        public double Alpha { get; set; } = 0.05;

        public double BetaCaution { get; set; } = 0.20;

        public double BetaFail { get; set; } = 0.05;

        public int SigmaFromN { get; set; } = 20;
    }

    internal sealed class CalibrationSection
    {
        public int IsotonicMinRows { get; set; } = 500;
    }

    internal sealed class LadderSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public int Epv { get; set; } = 15;

        public int MaxLogisticFeatures { get; set; } = 8;

        public int MinTreeEvents { get; set; } = 100;

        public double MinProbability { get; set; } = 0.95;

        public double PsiWatch { get; set; } = 0.10;

        public double PsiAlarm { get; set; } = 0.25;

        public int PsiBins { get; set; } = 10;

        public int MinRows { get; set; } = 30;

        public double MaxFlipShare { get; set; } = 1.0 / 3.0;
    }

    internal sealed class MlInfluenceSection
    {
        public int HoldOutMinRows { get; set; } = 100;

        public double HoldOutMinProbability { get; set; } = 0.95;

        public int ForwardMinCandidates { get; set; } = 30;

        public int ForwardMinWeeks { get; set; } = 4;

        public double ForwardMaxEcePp { get; set; } = 5;
    }

    internal sealed class PairedBootstrapSection
    {
        public ConfidenceMode Mode { get; set; } = ConfidenceMode.ReportOnly;

        public int Draws { get; set; } = 2000;

        public double Level { get; set; } = 0.90;

        public int MinDays { get; set; } = 10;

        public double PromisingFrom { get; set; } = 0.90;
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

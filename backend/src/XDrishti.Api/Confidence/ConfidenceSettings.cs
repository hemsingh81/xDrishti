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
}

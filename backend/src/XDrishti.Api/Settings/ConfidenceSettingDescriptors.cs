using System.Globalization;
using System.Reflection;
using XDrishti.Api.Confidence;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.Settings;

/// <summary>
/// One descriptor for every key of the <c>Confidence</c> section (AU-08, ADR 0242). The default is read from a fresh <see cref="ConfidenceSettings"/>, so it cannot drift from the real default. The bounds are the outer
/// bounds of the existing validation messages (inclusive; the existing validators keep the stricter rules). A key with no declared direction has <c>None</c> and so loosens both ways; the direction is declared only
/// where the existing validation or an ADR says the key may only be tightened (Readiness, Kill, MlInfluence, Decay, Loop Level and HelpsAt, the CostStress tier, capacity and turnover values; and by plain meaning of the gate Pbo MaxPbo, the largest probability of backtest overfitting a pass tolerates); when a direction is not provably safe it is None. A key is learnable only when it is on <see cref="LearnedAllowList"/> (AU-10).
/// </summary>
internal static class ConfidenceSettingDescriptors
{
    private const SettingSafer Up = SettingSafer.Higher;
    private const SettingSafer Down = SettingSafer.Lower;
    private const SettingSafer NoDirection = SettingSafer.None;

    /// <summary>The sections whose keys the owner may tighten: the readiness bars, the kill rule, the model influence bars, the decay trip and the cost stress values.</summary>
    private static readonly HashSet<string> AdvancedSections = new(StringComparer.Ordinal) { "Readiness", "Kill", "MlInfluence", "Decay", "CostStress" };

    /// <summary>The descriptors, in the order of the table below.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } = Build();

    private static List<SettingDescriptor> Build()
    {
        var defaults = new ConfidenceSettings();
        var list = new List<SettingDescriptor>();

        void K(string path, SettingSafer safer = NoDirection, double? min = null, double? max = null)
        {
            var section = path[..path.IndexOf(':', StringComparison.Ordinal)];
            var tier = AdvancedSections.Contains(section) ? SettingTier.Advanced : SettingTier.Internal;
            var key = ConfidenceSettings.SectionName + ":" + path;
            list.Add(new SettingDescriptor(key, Humanise(path), tier, SettingKind.Configuration, DefaultOf(defaults, path),
                min is null ? null : (decimal)min, max is null ? null : (decimal)max, safer, Learnable: LearnedAllowList.Keys.Contains(key)));
        }

        K("EffectiveN:Mode"); K("EffectiveN:MinSessionsForAutocorr", NoDirection, 5, 250); K("EffectiveN:MinTradesForEstimate", NoDirection, 3, 50); K("EffectiveN:MinClusters", NoDirection, 3, 50);
        K("Power:Alpha", NoDirection, 0.001, 0.2); K("Power:Power", NoDirection, 0.5, 0.99); K("Power:SigmaFloorR", NoDirection, 0.1, 5); K("Power:TargetEdgeR", NoDirection, 0.01, 5);
        K("MinTrl:Confidence", NoDirection, 0.5, 0.999); K("MinTrl:BenchmarkSr", NoDirection, -1, 1);
        K("Shrinkage:Mode"); K("Shrinkage:TauSquaredFloor", NoDirection, 0.0001, 1); K("Shrinkage:MinTradesForSibling", NoDirection, 3, 30); K("Shrinkage:MinParentTrades", NoDirection, 10, 200);
        K("Shrinkage:StrengthMin", NoDirection, 1, 1000); K("Shrinkage:StrengthMax", NoDirection, 1, 1000); K("Shrinkage:QuantileLevel", NoDirection, 0.01, 0.25); K("Shrinkage:SigmaPoolStrength", NoDirection, 1, 1000);
        K("Deflation:Mode"); K("Deflation:MinProbability", NoDirection, 0.5, 0.999); K("Deflation:AssumedTrialSrSd", NoDirection, 0.001, 1); K("Deflation:TBasis");
        K("Bootstrap:Mode"); K("Bootstrap:Draws", NoDirection, 100, 20000); K("Bootstrap:BlockLength", NoDirection, 0, 60);
        K("Pbo:Mode"); K("Pbo:Blocks", NoDirection, 4, 16); K("Pbo:MinVariants", NoDirection, 6, 100); K("Pbo:MaxPbo", Down, 0.1, 0.9); K("Pbo:Metric");
        K("CostStress:Mode"); K("CostStress:Enforce"); K("CostStress:MidMultiplier", NoDirection, 1, 5); K("CostStress:MidFloorR", NoDirection, -1, 1); K("CostStress:HighMultiplier", NoDirection, 1, 5); K("CostStress:HighFloorR", NoDirection, -1, 1);
        K("CostStress:DelayMinutes", NoDirection, 1, 5); K("CostStress:DelayRetention", NoDirection, 0.1, 1); K("CostStress:TierACutCrore", Up, 100, 100000); K("CostStress:TierBCutCrore", Up, 20, 100000);
        K("CostStress:CapacityFillShare", Down, 0, 0.01); K("CostStress:CapacityDailyShare", Down, 0, 0.005); K("CostStress:FillMinuteMaxTrades", NoDirection, 0, 20000); K("CostStress:FillMinuteTimeoutSeconds", NoDirection, 1, 600);
        K("CostStress:TierABps", Up, 3, 5); K("CostStress:TierBBps", Up, 8, 15); K("CostStress:RangeFraction", Up, 0.10, 0.20); K("CostStress:MinTurnoverCrore", Up, 20, 50);
        K("Nulls:Mode"); K("Nulls:Enforce"); K("Nulls:Draws", NoDirection, 200, 20000); K("Nulls:Percentile", NoDirection, 90, 99); K("Nulls:Redraws", NoDirection, 0, 20); K("Nulls:Placebos"); K("Nulls:MinPlacebos", NoDirection, 19, 20); K("Nulls:MaxDraws", NoDirection, 1000, 20000);
        K("Plateau:Mode"); K("Plateau:Enforce"); K("Plateau:Retention", NoDirection, 0.5, 0.95); K("Plateau:MinPoints", NoDirection, 3, 5);
        K("Sequential:Mu1", NoDirection, 0, 2); K("Sequential:Sigma", NoDirection, 0.5, 5); K("Sequential:Alpha", NoDirection, 0, 0.2); K("Sequential:BetaCaution", NoDirection, 0, 0.5); K("Sequential:BetaFail", NoDirection, 0, 0.5); K("Sequential:SigmaFromN", NoDirection, 5, 200);
        K("Calibration:IsotonicMinRows", NoDirection, 1, 100000);
        K("Ladder:Mode"); K("Ladder:Epv", NoDirection, 5, 50); K("Ladder:MaxLogisticFeatures", NoDirection, 1, 50); K("Ladder:MinTreeEvents", NoDirection, 10, 100000); K("Ladder:MinProbability", NoDirection, 0.5, 0.999);
        K("Ladder:PsiWatch", NoDirection, 0, 5); K("Ladder:PsiAlarm", NoDirection, 0, 5); K("Ladder:PsiBins", NoDirection, 2, 100); K("Ladder:MinRows", NoDirection, 5, 100000); K("Ladder:MaxFlipShare", NoDirection, 0, 0.5);
        K("PairedBootstrap:Mode"); K("PairedBootstrap:Draws", NoDirection, 100, 20000); K("PairedBootstrap:Level", NoDirection, 0.5, 1); K("PairedBootstrap:MinDays", NoDirection, 2, 1000); K("PairedBootstrap:PromisingFrom", NoDirection, 0.5, 0.99);
        K("MlInfluence:HoldOutMinRows", Up, 100, 100000); K("MlInfluence:HoldOutMinProbability", Up, 0.95, 0.999); K("MlInfluence:ForwardMinCandidates", Up, 30, 100000); K("MlInfluence:ForwardMinWeeks", Up, 4, 520); K("MlInfluence:ForwardMaxEcePp", Down, 0, 5);
        K("Decay:WindowTrades", NoDirection, 20, 60); K("Decay:Sigmas", Down, 0, 1.5); K("Decay:QuarterFloorR", Up, -0.05, 0);
        K("Loop:MinDays", NoDirection, 20, 250); K("Loop:Draws", NoDirection, 2000, 20000); K("Loop:Level", Up, 0.90, 0.99); K("Loop:HelpsAt", Up, 0.95, 0.999); K("Loop:MinSlopePer50Rows", NoDirection, 0, 0.1);
        K("Loop:TargetGain", NoDirection, 0.001, 0.1); K("Loop:SlopeLow", NoDirection, 0.8, 1); K("Loop:SlopeHigh", NoDirection, 1, 1.2); K("Loop:SlopeSe", NoDirection, 1, 2);
        K("Kill:StressMultiplier", Up, 1.5, 3); K("Kill:DrawdownFactor", Down, 1, 2);
        K("Registration:Enforce"); K("Registration:CountInFactor");
        K("HoldOut:Enforce"); K("HoldOut:AutoUnlock"); K("HoldOut:MinMonths", NoDirection, 1, 60); K("HoldOut:SharePercent", NoDirection, 5, 50); K("HoldOut:MinTrades", NoDirection, 5, 1000); K("HoldOut:Retention", NoDirection, 0.1, 1); K("HoldOut:TrainingEmbargoDays", NoDirection, 28, 180);
        K("Readiness:MinNEff", Up, 300, 100000); K("Readiness:MinTradesPerCell", Up, 30, 10000); K("Readiness:ExpectancyR", Up, 0.15, 2); K("Readiness:TStatistic", Up, 2, 10); K("Readiness:MinQuarterShare", Up, 0.70, 1);
        K("Readiness:MaxYearShare", Down, 0.20, 0.60); K("Readiness:MaxInstrumentShare", Down, 0.02, 0.15); K("Readiness:DrawdownBudgetR", Down, 1, 20);
        K("Readiness:BuildingFrom", Up, 40, 99); K("Readiness:UsableFrom", Up, 70, 99.5); K("Readiness:ReadyFrom", Up, 85, 100);

        return list;
    }

    /// <summary>The property of the settings object at a path such as <c>Pbo:MaxPbo</c>.</summary>
    internal static PropertyInfo? PropertyAt(object root, string path, out object? holder)
    {
        holder = root;
        var parts = path.Split(':');
        PropertyInfo? property = null;
        for (var i = 0; i < parts.Length; i++)
        {
            property = holder!.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                return null;
            }

            if (i < parts.Length - 1)
            {
                holder = property.GetValue(holder);
            }
        }

        return property;
    }

    /// <summary>The value at a path of the Confidence settings as invariant text, or null for a key left unset.</summary>
    internal static string? ValueOf(ConfidenceSettings settings, string path)
    {
        var property = PropertyAt(settings, path, out var holder);
        return property?.GetValue(holder) is { } value ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private static string? DefaultOf(ConfidenceSettings defaults, string path) => ValueOf(defaults, path);

    private static string Humanise(string path)
    {
        var text = path.Replace(':', ' ');
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }
}

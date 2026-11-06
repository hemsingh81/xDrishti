using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>The inputs of the CF-11 tests: all-pass and perfect baselines to vary one value at a time.</summary>
internal static class ReadinessInputsFixture
{
    /// <summary>Every row passes: 320 independent trades, +0.20 R, sd 1 R, Factor 1.25.</summary>
    public static ChecklistInputs Passing() => new()
    {
        Registered = true,
        NEff = 320,
        MinTradesPerCell = 30,
        ExpectancyR = 0.20,
        SdR = 1.0,
        Factor = 1.25,
        CostStress15 = RowStatus.Pass,
        CostStress2 = RowStatus.Pass,
        FillDelay = RowStatus.Pass,
        RandomEntry = RowStatus.Pass,
        Placebo = RowStatus.Pass,
        Mirror = RowStatus.Pass,
        Plateau = RowStatus.Pass,
        QuarterShare = 0.80,
        MaxYearShare = 0.50,
        MaxInstrumentShare = 0.10,
        ObservedMaxDrawdownR = 5,
        McDrawdownP90 = 8,
        McDrawdownP95 = 12,
        HoldOut = RowStatus.Pass,
        BenchmarkBeaten = true,
        CapacityOk = true,
        TwentyTradeCheckPassed = true,
        PhaseCheckMet = true,
        ForwardMeanR = 0.20,
        ForwardStandardError = 0.05,
    };

    /// <summary>Every sub-score is 100.</summary>
    public static ScoreInputs Perfect() => new()
    {
        ForwardNEff = 200,
        ForwardMeanR = 0.5,
        ForwardSdR = 1.0,
        Band = BandPosition.InsideBand,
        PositiveMonthShare = 0.8,
        ModelApplies = true,
        EcePp = 2,
        Folds = 6,
        PositiveFolds = 6,
        DriftAlarm = false,
    };

    public static ReadinessOptions Options { get; } = new();
}

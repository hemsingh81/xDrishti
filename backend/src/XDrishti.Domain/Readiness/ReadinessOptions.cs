using System.Globalization;

namespace XDrishti.Domain.Readiness;

/// <summary>
/// The <c>Confidence:Readiness</c> settings of CF-11 part 1 (ADR 0221, spec D1 to D4): the thresholds of the edge checklist that the scorecard judges itself (the rows another slice judges, such as the cost stress, the nulls and the hold-out, keep that slice's own settings) and the three
/// label cut-offs. Every value is the spec's proposal at its loose end and can only be moved to the <b>stricter</b> side: a higher count or expectancy bar, a lower share ceiling, a lower drawdown budget, a higher label cut-off. That is true in both directions of use: a stricter
/// row fails more often (Gate R and Gate S refuse more, never fewer), and a higher label cut-off gives a lower label for the same score. The sub-score anchors (D2) are not settings: they interact (a change of one moves the meaning of the others), so a change is a code change with an ADR.
/// The enforce switch is not here: it is stored state that only an owner act changes. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="MinNEff">Row 2: the fewest independent (effective) trades of the out-of-sample set (default 300; 300 to 100000).</param>
/// <param name="MinTradesPerCell">Row 2: the fewest trades of every used cell (default 30; 30 to 10000).</param>
/// <param name="ExpectancyR">Row 3: the net expectancy at 1x costs must be at least this times the multiple-testing factor (default 0.15 R; 0.15 to 2).</param>
/// <param name="TStatistic">Row 6: the t-statistic must be at least this times the factor (default 2.0; 2 to 10).</param>
/// <param name="MinQuarterShare">Row 10: the share of calendar quarters at or above the quarter floor (default 0.70; 0.70 to 1).</param>
/// <param name="MaxYearShare">Row 10: no year may hold more than this share of the total R (default 0.60; 0.20 to 0.60).</param>
/// <param name="MaxInstrumentShare">Row 10: no instrument may hold more than this share of the total R (default 0.15; 0.02 to 0.15).</param>
/// <param name="DrawdownBudgetR">Row 11: the Monte Carlo 95th percentile of the drawdown must stay under this many R (default 20; 1 to 20).</param>
/// <param name="BuildingFrom">The score from which the label is "Building" (default 40; 40 to 99).</param>
/// <param name="UsableFrom">The score from which the label is "Usable with advice" (default 70; 70 to 99.5).</param>
/// <param name="ReadyFrom">The score from which the label is "Ready for the next phase" (default 85; 85 to 100).</param>
public sealed record ReadinessOptions(
    double MinNEff = ReadinessOptions.DefaultMinNEff,
    int MinTradesPerCell = ReadinessOptions.DefaultMinTradesPerCell,
    double ExpectancyR = ReadinessOptions.DefaultExpectancyR,
    double TStatistic = ReadinessOptions.DefaultTStatistic,
    double MinQuarterShare = ReadinessOptions.DefaultMinQuarterShare,
    double MaxYearShare = ReadinessOptions.DefaultMaxYearShare,
    double MaxInstrumentShare = ReadinessOptions.DefaultMaxInstrumentShare,
    double DrawdownBudgetR = ReadinessOptions.DefaultDrawdownBudgetR,
    double BuildingFrom = ReadinessOptions.DefaultBuildingFrom,
    double UsableFrom = ReadinessOptions.DefaultUsableFrom,
    double ReadyFrom = ReadinessOptions.DefaultReadyFrom)
{
    /// <summary>Row 2 proposal.</summary>
    public const double DefaultMinNEff = 300;

    /// <summary>Row 2 proposal.</summary>
    public const int DefaultMinTradesPerCell = 30;

    /// <summary>Row 3 proposal.</summary>
    public const double DefaultExpectancyR = 0.15;

    /// <summary>Row 6 proposal.</summary>
    public const double DefaultTStatistic = 2.0;

    /// <summary>Row 10 proposal.</summary>
    public const double DefaultMinQuarterShare = 0.70;

    /// <summary>Row 10 proposal.</summary>
    public const double DefaultMaxYearShare = 0.60;

    /// <summary>Row 10 proposal.</summary>
    public const double DefaultMaxInstrumentShare = 0.15;

    /// <summary>Row 11 proposal.</summary>
    public const double DefaultDrawdownBudgetR = 20;

    /// <summary>Label proposal (D4).</summary>
    public const double DefaultBuildingFrom = 40;

    /// <summary>Label proposal (D4).</summary>
    public const double DefaultUsableFrom = 70;

    /// <summary>Label proposal (D4).</summary>
    public const double DefaultReadyFrom = 85;

    /// <summary>The version of the fixed anchors and rules that are not settings; it is part of the settings text, so a change of the code's rules changes every snapshot's hash.</summary>
    public const string RulesVersion = "cf11-v1";

    /// <summary>The text of the values, kept with every snapshot so it says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture,
        $"{RulesVersion}/nEff={MinNEff:R}/perCell={MinTradesPerCell}/expectancy={ExpectancyR:R}/t={TStatistic:R}/quarters={MinQuarterShare:R}/year={MaxYearShare:R}/instrument={MaxInstrumentShare:R}/drawdown={DrawdownBudgetR:R}/labels={BuildingFrom:R},{UsableFrom:R},{ReadyFrom:R}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Check(problems, MinNEff, DefaultMinNEff, 100_000, "Readiness:MinNEff must be 300 to 100000 (it can only be raised)");
        Check(problems, MinTradesPerCell, DefaultMinTradesPerCell, 10_000, "Readiness:MinTradesPerCell must be 30 to 10000 (it can only be raised)");
        Check(problems, ExpectancyR, DefaultExpectancyR, 2, "Readiness:ExpectancyR must be 0.15 to 2 (it can only be raised)");
        Check(problems, TStatistic, DefaultTStatistic, 10, "Readiness:TStatistic must be 2 to 10 (it can only be raised)");
        Check(problems, MinQuarterShare, DefaultMinQuarterShare, 1, "Readiness:MinQuarterShare must be 0.70 to 1 (it can only be raised)");
        Check(problems, MaxYearShare, 0.20, DefaultMaxYearShare, "Readiness:MaxYearShare must be 0.20 to 0.60 (it can only be lowered)");
        Check(problems, MaxInstrumentShare, 0.02, DefaultMaxInstrumentShare, "Readiness:MaxInstrumentShare must be 0.02 to 0.15 (it can only be lowered)");
        Check(problems, DrawdownBudgetR, 1, DefaultDrawdownBudgetR, "Readiness:DrawdownBudgetR must be 1 to 20 (it can only be lowered)");
        Check(problems, BuildingFrom, DefaultBuildingFrom, 99, "Readiness:BuildingFrom must be 40 to 99 (it can only be raised)");
        Check(problems, UsableFrom, DefaultUsableFrom, 99.5, "Readiness:UsableFrom must be 70 to 99.5 (it can only be raised)");
        Check(problems, ReadyFrom, DefaultReadyFrom, 100, "Readiness:ReadyFrom must be 85 to 100 (it can only be raised)");
        if (double.IsFinite(BuildingFrom) && double.IsFinite(UsableFrom) && double.IsFinite(ReadyFrom) && !(BuildingFrom < UsableFrom && UsableFrom < ReadyFrom))
        {
            problems.Add("Readiness: BuildingFrom, UsableFrom and ReadyFrom must be strictly increasing");
        }

        return problems.AsReadOnly();
    }

    private static void Check(List<string> problems, double value, double low, double high, string message)
    {
        if (!double.IsFinite(value) || value < low || value > high)
        {
            problems.Add(message);
        }
    }
}

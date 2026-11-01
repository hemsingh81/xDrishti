namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// The numbers the checks judge by (spec P14-04, ADR 0178). The defaults are the drafted ones; the application binds <c>Options:Quality:*</c> over them. Shares are fractions (0.95 = 95 %); prices are paise; <see cref="Rate"/> is a yearly rate (0.065 = 6.5 %, UNVERIFIED).
/// </summary>
public sealed record OptionQualityThresholds
{
    public double CoveragePass { get; init; } = 0.95;
    public double CoverageWarn { get; init; } = 0.80;

    public double Rate { get; init; } = 0.065;
    public double ParityPass { get; init; } = 0.90;
    public double ParityWarn { get; init; } = 0.70;
    public int TickPaise { get; init; } = 5;
    public int MinPoints { get; init; } = 5;

    public double MaxIv { get; init; } = 300;
    public double DeltaTolerance { get; init; } = 0.02;
    public double CalendarTolerance { get; init; } = 0.05;
    public double GreeksWarn { get; init; } = 0.02;
    public double GreeksFail { get; init; } = 0.10;

    public double OiJumpPct { get; init; } = 50;
    public int OiJumpMinLots { get; init; } = 10;
    public double OiWarnShare { get; init; } = 0.01;

    public int StrikeJumpSteps { get; init; } = 2;
    public double StitchFailShare { get; init; } = 0.01;
    public double SpotTolerance { get; init; } = 0.005;
    public double SpotWarnShare { get; init; } = 0.01;
}

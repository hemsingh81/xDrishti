using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.OptionQuality;

/// <summary>The settings <c>Options:Quality:*</c> of the option-archive quality run (P14-04, ADR 0178), bound on <c>xd-api</c>; bounds are checked at start-up. Shares are fractions.</summary>
internal sealed class OptionQualityOptions
{
    public const string SectionName = "Options:Quality";

    /// <summary>The yearly risk-free rate of the parity check (UNVERIFIED; the owner may set it).</summary>
    [Range(0.0, 0.2)]
    public double Rate { get; set; } = 0.065;

    [Range(1.0, 1000.0)]
    public double MaxIv { get; set; } = 300;

    [Range(1.0, 1000.0)]
    public double OiJumpPct { get; set; } = 50;

    [Range(0.5, 1.0)]
    public double CoveragePass { get; set; } = 0.95;

    [Range(0.1, 1.0)]
    public double CoverageWarn { get; set; } = 0.80;

    [Range(0.5, 1.0)]
    public double ParityPass { get; set; } = 0.90;

    [Range(0.1, 1.0)]
    public double ParityWarn { get; set; } = 0.70;

    [Range(0.0, 0.5)]
    public double GreeksWarn { get; set; } = 0.02;

    [Range(0.0, 1.0)]
    public double GreeksFail { get; set; } = 0.10;

    [Range(0.0, 0.5)]
    public double OiWarnShare { get; set; } = 0.01;

    [Range(0, 1000)]
    public int OiJumpMinLots { get; set; } = 10;

    [Range(1, 100)]
    public int TickPaise { get; set; } = 5;

    [Range(1, 60)]
    public int LookbackDays { get; set; } = 10;

    [Range(1, 400)]
    public int MaxChainSessionsPerRun { get; set; } = 20;

    [Range(1, 5000)]
    public int MaxRollingWindowsPerRun { get; set; } = 5000;

    [Range(1, 120)]
    public int MaxRunMinutes { get; set; } = 30;
}

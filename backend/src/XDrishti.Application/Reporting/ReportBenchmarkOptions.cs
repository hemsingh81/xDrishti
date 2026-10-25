using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Reporting;

/// <summary>Settings of the basket benchmarks (configuration section <c>Reports:Benchmark</c>, P8-07, ADR 0102).</summary>
public sealed class ReportBenchmarkOptions
{
    public const string SectionName = "Reports:Benchmark";

    /// <summary>Fewest included instruments for a buy-and-hold number.</summary>
    [Range(1, 100)]
    public int MinMembers { get; set; } = 3;

    /// <summary>Most days the first bar may follow the window start (or the last precede its end).</summary>
    [Range(0, 60)]
    public int MaxStartGapDays { get; set; } = 7;

    /// <summary>Largest one-session close-to-close move kept (0.25 = 25 %).</summary>
    [Range(0.01, 10.0)]
    public double DiscontinuityThreshold { get; set; } = 0.25;

    /// <summary>Most instruments one basket may hold for this read.</summary>
    [Range(1, 5000)]
    public int MaxInstruments { get; set; } = 300;

    /// <summary>Most baskets one call may name.</summary>
    [Range(1, 100)]
    public int MaxBaskets { get; set; } = 12;
}

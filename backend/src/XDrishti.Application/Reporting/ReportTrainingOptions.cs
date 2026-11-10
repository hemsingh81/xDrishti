using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Reporting;

/// <summary>Settings of the reports over a simulate-everything job (configuration section <c>Reports:Training</c>, AU-14, ADR 0250).</summary>
public sealed class ReportTrainingOptions
{
    public const string SectionName = "Reports:Training";

    /// <summary>
    /// Most closed trades one training report reads before it refuses (422 <c>reports.too_large</c>), never cuts. The default is the cap of the evidence reports (ADR 0100), which was measured against the 1 GiB limit of the API container with one report in memory at a time; raise it
    /// only after measuring. A report of one stock or one basket stays far below it; a whole-universe report over a big job does not, and is asked to narrow.
    /// </summary>
    [Range(10_000, 1_000_000)]
    public int MaxTrades { get; set; } = 500_000;
}

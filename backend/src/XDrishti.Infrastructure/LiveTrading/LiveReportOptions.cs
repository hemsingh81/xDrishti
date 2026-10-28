using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary><c>Live:Report</c> (spec P11-03): the non-inferiority margin in R, the fewest pairs for a verdict, and the paper account to pair with when several exist. Thresholds only: the report is read only and no setting here switches a behaviour.</summary>
public sealed class LiveReportOptions
{
    public const string SectionName = "Live:Report";

    [Range(0.01, 0.50)]
    public double NonInferiorityR { get; set; } = 0.10;

    [Range(10, 10000)]
    public int MinPairs { get; set; } = 30;

    public Guid? PaperAccountId { get; set; }
}

/// <summary>The settings port over <c>Live:Report</c>.</summary>
internal sealed class ConfiguredLiveReportSettings(IOptions<LiveReportOptions> options) : ILiveReportSettings
{
    public double NonInferiorityR => options.Value.NonInferiorityR;

    public int MinPairs => options.Value.MinPairs;

    public Guid? PaperAccountId => options.Value.PaperAccountId;
}

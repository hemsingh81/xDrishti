using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Reporting;

/// <summary>Settings of plan versus actual (configuration section <c>Reports:PlanVsActual</c>, P8-04).</summary>
public sealed class PlanVsActualSettings
{
    public const string SectionName = "Reports:PlanVsActual";

    /// <summary>Fewest tickets graded on fills for a number in the chain or by strategy.</summary>
    [Range(1, 1000)]
    public int MinTickets { get; set; } = 15;

    /// <summary>Longest date range in days.</summary>
    [Range(1, 3700)]
    public int MaxRangeDays { get; set; } = 366;
}

using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>The configuration section <c>Learning:CellsRefresh</c> (spec LC-01, D8, Internal tier): when the bell says the cell statistics are getting old. A value out of range fails start-up.</summary>
public sealed class CellsRefreshOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Learning:CellsRefresh";

    /// <summary>The bell speaks while the nightly plan is enabled and the newest as-of date is at least this many sessions behind the last closed session (the gate fails above 5).</summary>
    [Range(1, 4)]
    public int NoticeAfterSessions { get; set; } = CellsRefreshRules.DefaultNoticeAfterSessions;
}

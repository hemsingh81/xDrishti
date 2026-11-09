using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Settings of the recovery of missed days (configuration section <c>Recovery</c>, ADR 0240). Not a visible setting: a validated default; an invalid value stops the start-up.</summary>
public sealed class RecoverySettings
{
    public const string SectionName = "Recovery";

    /// <summary>How many of the latest trading sessions are made up for; older ones are reported out of reach.</summary>
    [Range(RecoveryPlanner.MinSessions, RecoveryPlanner.MaxSessionsLimit)]
    public int MaxSessions { get; set; } = RecoveryPlanner.DefaultMaxSessions;
}

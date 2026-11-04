using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

/// <summary>The settings <c>Options:PhaseCheck:*</c> (P14-17, ADR 0202), bound on every host that runs the phase check; bounds are checked at start-up.</summary>
internal sealed class OptionsPhaseCheckOptions
{
    public const string SectionName = "Options:PhaseCheck";

    /// <summary>After how many days an archive watermark makes a stored verdict stale (7 to 730).</summary>
    [Required]
    [Range(7, 730)]
    public int? StaleAfterDays { get; set; } = 90;
}

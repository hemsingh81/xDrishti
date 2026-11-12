using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Settings of the shadow record that are not the owner's switch (configuration section <c>Shadow</c>, ADR 0232). Both are validated at start. Neither turns the record on: the switch <c>paper.shadow.enabled</c> stays OFF until the owner changes it.
/// </summary>
public sealed class ShadowOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string SectionName = "Shadow";

    /// <summary>
    /// The folder outside the database where the weekly chain digest is appended (<c>shadow-digest.log</c>); relative paths are relative to the working directory, so the <c>xd</c> script sets an absolute one. Keep it on a disk the database role cannot write
    /// (a different volume or a backup mount) so that a database owner cannot rewrite the chain and its copy together.
    /// </summary>
    [Required]
    public string DigestFolder { get; set; } = "data/shadow-digest";

    /// <summary>
    /// How many trading sessions that have already opened (and so become gap rows, never back-filled) a switch-ON may skip without the owner's acknowledgement. Above it the switch change is refused with 409 <c>paper.shadow_gap</c> until the request says it
    /// accepts the gap. 0 (the default) means any missed session needs the acknowledgement.
    /// </summary>
    [Range(0, 60)]
    public int MaxResumeGapSessions { get; set; }

    /// <summary>
    /// Scoring of the candidates (CF-10e) starts only this many minutes before the 09:00 India cut-off of the session; later, the session is written without scores so that the chain never depends on the models (the database refuses a score from 09:00 on). Default 30, so scoring
    /// must start before 08:30.
    /// </summary>
    [Range(1, 600)]
    public int ScoringDeadlineMinutesBeforeCutoff { get; set; } = 30;
}

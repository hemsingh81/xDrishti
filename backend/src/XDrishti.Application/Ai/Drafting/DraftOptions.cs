using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>Settings <c>Assistant:Drafts</c> (spec P9-07). Config only. <see cref="Enabled"/> is OFF by default and also needs <c>ai.settings.enabled</c>.</summary>
public sealed class DraftOptions
{
    public const string SectionName = "Assistant:Drafts";

    public bool Enabled { get; set; }

    [Range(1, 200)]
    public int MaxPerDay { get; set; } = 20;

    [Range(0, 3)]
    public int MaxRepairs { get; set; } = 2;

    [Range(30, 1800)]
    public int JobTimeoutSeconds { get; set; } = 360;

    public long Seed { get; set; } = 1337;
}

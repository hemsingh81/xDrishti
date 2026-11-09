using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The guardrails of learned changes (AU-10, ADR 0247, bound from <c>Learned</c>). <c>WeeklyCap</c> is the most changes learning may apply by itself in a trailing week (0 turns automatic application off). <c>MaxStepPercent</c> is the
/// most one automatic change may move a value, as a share of the value it replaces: a bounded step keeps one week's evidence from jumping a gate, and the next week can step again from new evidence.
/// </summary>
public sealed class LearnedSettingsOptions
{
    public const string SectionName = "Learned";

    [Range(0, 10)]
    public int WeeklyCap { get; set; } = 3;

    [Range(1, 25)]
    public int MaxStepPercent { get; set; } = 10;

    /// <summary>The most all automatic and proposed tightenings together may move a value away from the configured one, as a share of the configured value (stacked steps cannot drift without limit).</summary>
    [Range(1, 100)]
    public int MaxTotalPercent { get; set; } = 30;
}

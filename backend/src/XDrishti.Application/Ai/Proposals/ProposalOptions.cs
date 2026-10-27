using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>Settings <c>Assistant:Proposals</c> (spec P9-08 Contract). Config only. <see cref="Enabled"/> is OFF by default and generation and the auto option also need <c>ai.settings.enabled</c>.</summary>
public sealed class ProposalOptions
{
    public const string SectionName = "Assistant:Proposals";

    public bool Enabled { get; set; }

    [Range(1, 10)]
    public int MaxPerRun { get; set; } = 3;

    [Range(1, 50)]
    public int MaxPending { get; set; } = 10;

    [Range(1, 10)]
    public int MaxPendingHypotheses { get; set; } = 3;

    [Range(1, 60)]
    public int HypothesisExpiryDays { get; set; } = 7;

    [Range(1, 60)]
    public int BasketExpiryDays { get; set; } = 3;

    [Range(1, 365)]
    public int RepeatBlockDays { get; set; } = 30;

    [Range(1, 50)]
    public int MaxKnobMovePct { get; set; } = 50;

    /// <summary>The most hypotheses the auto option accepts per India date.</summary>
    [Range(1, 10)]
    public int AutoPerDay { get; set; } = 3;

    [Range(1, 5)]
    public int PackCells { get; set; } = 5;

    [Range(1, 5)]
    public int PackBaskets { get; set; } = 5;

    public long Seed { get; set; } = 1337;
}

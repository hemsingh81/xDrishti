using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>The kill proposals, newest first (spec CF-12, part 12b). Read only.</summary>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>; null for all.</param>
/// <param name="Strategy">One strategy, or null for all.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record GetKillProposalsQuery(string? State, string? Strategy, int Limit = 50) : IQuery<Result<KillProposalsView>>;

/// <summary>One kill proposal as the owner reads it.</summary>
/// <param name="Id">The proposal.</param>
/// <param name="Strategy">The strategy.</param>
/// <param name="Rule"><c>cost_stress</c>, <c>drawdown</c>, <c>decay_twice</c> or <c>failure_boundary</c>.</param>
/// <param name="ProposalOnly">True for the CF-09 failure boundary: the automatic setting never executes it, only the owner applies it.</param>
/// <param name="Value">The figure judged.</param>
/// <param name="Limit">What it was judged against.</param>
/// <param name="Trades">Trades the figure is based on.</param>
/// <param name="Text">The words, with the numbers.</param>
/// <param name="Cells">The cells it would pause (Paper or Active when proposed).</param>
/// <param name="SettingsHash">The SHA-256 of the settings that produced it.</param>
/// <param name="ProposedOn">The India date it was proposed.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>.</param>
/// <param name="Actor"><c>owner:...</c> or <c>auto</c> once applied, else null.</param>
/// <param name="DecidedAt">When it was decided, or null.</param>
/// <param name="AutoSettingId">For an automatic decision, the switch row in force; else null.</param>
/// <param name="DismissReason">The owner's reason when the proposal was dismissed, else null.</param>
public sealed record KillProposalView(
    long Id,
    string Strategy,
    string Rule,
    bool ProposalOnly,
    double? Value,
    double? Limit,
    int Trades,
    string Text,
    IReadOnlyList<string> Cells,
    string SettingsHash,
    DateOnly ProposedOn,
    DateTimeOffset ProposedAt,
    string State,
    string? Actor,
    DateTimeOffset? DecidedAt,
    long? AutoSettingId,
    string? DismissReason = null);

/// <summary>The automatic setting and the thresholds as they stand.</summary>
/// <param name="Auto">Whether an automatic demotion is allowed now (default OFF).</param>
/// <param name="SettingId">The row of the switch in force, or null when none was ever stored.</param>
/// <param name="StressMultiplier">The cost multiplier of rule (a).</param>
/// <param name="DrawdownFactor">The factor of rule (b).</param>
/// <param name="SettingsHash">The SHA-256 of the thresholds.</param>
public sealed record KillSettingsView(bool Auto, long? SettingId, double StressMultiplier, double DrawdownFactor, string SettingsHash);

/// <summary>The proposals and the settings beside them.</summary>
/// <param name="Items">The proposals, newest first.</param>
/// <param name="Settings">The automatic setting and the thresholds.</param>
/// <param name="Statements">The honesty sentences.</param>
public sealed record KillProposalsView(IReadOnlyList<KillProposalView> Items, KillSettingsView Settings, IReadOnlyList<string> Statements);

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The rollback proposals, newest first (spec CF-10 D8, part 10d-1). Read only.</summary>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>; null for all.</param>
/// <param name="ModelId">One model, or null for all.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record GetRollbackProposalsQuery(string? State, long? ModelId, int Limit = 50) : IQuery<Result<RollbackProposalsView>>;

/// <summary>One rollback proposal as the owner reads it.</summary>
/// <param name="Id">The proposal.</param>
/// <param name="ModelId">The champion it would demote to challenger (shadow).</param>
/// <param name="Trigger"><c>calibration</c> (the P6-09 calibration detector) or <c>band</c> (the P6-09 rolling band detector).</param>
/// <param name="Value">The statistic of the alarm.</param>
/// <param name="Limit">What it was compared with.</param>
/// <param name="Text">The detector's words, with the numbers.</param>
/// <param name="Cells">For the band trigger, the cells in alarm.</param>
/// <param name="PreviousChampionId">The champion this model replaced, or null.</param>
/// <param name="PreviousQualifies">Whether that model's latest evaluation satisfied the predicate without the switch when the proposal was written. Information only: it is never re-promoted automatically.</param>
/// <param name="SettingsHash">The SHA-256 of the thresholds that produced it.</param>
/// <param name="ProposedOn">The India date it was proposed.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>.</param>
/// <param name="Actor"><c>owner:...</c> or <c>auto</c> once applied, else null.</param>
/// <param name="DecidedAt">When it was decided, or null.</param>
/// <param name="AutoSettingId">For an automatic decision, the row of the <c>ml.auto_rollback</c> switch in force; else null.</param>
public sealed record RollbackProposalView(
    long Id,
    long ModelId,
    string Trigger,
    double? Value,
    double? Limit,
    string Text,
    IReadOnlyList<string> Cells,
    long? PreviousChampionId,
    bool PreviousQualifies,
    string SettingsHash,
    DateOnly ProposedOn,
    DateTimeOffset ProposedAt,
    string State,
    string? Actor,
    DateTimeOffset? DecidedAt,
    long? AutoSettingId);

/// <summary>The three ML switches as they stand (all default OFF).</summary>
/// <param name="InfluenceEnabled"><c>ml.influence.enabled</c>.</param>
/// <param name="InfluenceSettingId">Its row in force, or null when none was ever stored.</param>
/// <param name="AutoPromote"><c>ml.auto_promote</c>.</param>
/// <param name="AutoPromoteSettingId">Its row in force, or null.</param>
/// <param name="AutoRollback"><c>ml.auto_rollback</c>.</param>
/// <param name="AutoRollbackSettingId">Its row in force, or null.</param>
/// <param name="AutoHoldOutScore"><c>ml.holdout.auto_score</c> (ON by the ADR 0282 seed row).</param>
/// <param name="AutoHoldOutScoreSettingId">Its row in force, or null.</param>
public sealed record MlSettingsView(bool InfluenceEnabled, long? InfluenceSettingId, bool AutoPromote, long? AutoPromoteSettingId, bool AutoRollback, long? AutoRollbackSettingId, bool AutoHoldOutScore = false, long? AutoHoldOutScoreSettingId = null);

/// <summary>The proposals and the switches beside them.</summary>
/// <param name="Items">The proposals, newest first.</param>
/// <param name="Settings">The three switches.</param>
/// <param name="Statements">The honesty sentences.</param>
public sealed record RollbackProposalsView(IReadOnlyList<RollbackProposalView> Items, MlSettingsView Settings, IReadOnlyList<string> Statements);

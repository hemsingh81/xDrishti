namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A rollback proposal to store (spec CF-10 D8, part 10d-1, ADR 0220).</summary>
/// <param name="ModelId">The champion the proposal would demote.</param>
/// <param name="Trigger"><c>calibration</c> or <c>band</c>.</param>
/// <param name="Value">The statistic of the alarm.</param>
/// <param name="Limit">What it was compared with.</param>
/// <param name="Text">The detector's own words, with the numbers.</param>
/// <param name="Cells">For the band trigger, the cells in alarm; empty otherwise.</param>
/// <param name="PreviousChampionId">The champion this model replaced, or null when it replaced none.</param>
/// <param name="PreviousQualifies">Whether the previous champion's latest evaluation satisfies the predicate without the switch. Never promotes it: re-promotion is the owner's act.</param>
/// <param name="SettingsHash">The SHA-256 of the thresholds that produced it.</param>
public sealed record NewRollbackProposal(long ModelId, string Trigger, double? Value, double? Limit, string Text, IReadOnlyList<string> Cells, long? PreviousChampionId, bool PreviousQualifies, string SettingsHash);

/// <summary>A stored rollback proposal.</summary>
/// <param name="Id">The row.</param>
/// <param name="Proposal">What was proposed.</param>
/// <param name="ProposedOn">The India date it was proposed (one per model, trigger and day).</param>
/// <param name="ProposedAt">When (the database clock).</param>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>.</param>
/// <param name="Actor"><c>owner:...</c> or <c>auto</c> once decided, else null.</param>
/// <param name="DecidedAt">When it was decided, or null.</param>
/// <param name="AutoSettingId">For an automatic decision, the row of <c>research.ml_switch_settings</c> (<c>ml.auto_rollback</c>) in force; else null.</param>
public sealed record RollbackProposalRow(long Id, NewRollbackProposal Proposal, DateOnly ProposedOn, DateTimeOffset ProposedAt, string State, string? Actor, DateTimeOffset? DecidedAt, long? AutoSettingId);

/// <summary>What the record said to a decision.</summary>
public enum RollbackDecideResult
{
    /// <summary>The proposal was <c>proposed</c> and is decided now.</summary>
    Decided,

    /// <summary>The proposal was decided already (by another caller), or does not exist.</summary>
    NotProposed,

    /// <summary>The record refuses the decision: an automatic decision while the newest <c>ml.auto_rollback</c> row is not ON or is not the one recorded, or an automatic dismissal. Nothing was changed.</summary>
    Refused,
}

/// <summary>Reads rollback proposals (<c>research.rollback_proposals</c>). Read only.</summary>
public interface IRollbackProposalReader
{
    /// <summary>One proposal, or null.</summary>
    ValueTask<RollbackProposalRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest proposals first, optionally of one state and one model.</summary>
    ValueTask<IReadOnlyList<RollbackProposalRow>> ListAsync(string? state, long? modelId, int limit, CancellationToken cancellationToken);
}

/// <summary>Writes rollback proposals: insert, and one compare-and-set of the state. Nothing else is ever changed.</summary>
public interface IRollbackProposalStore
{
    /// <summary>Stores a proposal; null when the model already has one for this trigger on this day (idempotent).</summary>
    ValueTask<long?> InsertAsync(NewRollbackProposal proposal, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Moves a <c>proposed</c> proposal to <paramref name="state"/> (<c>applied</c> or <c>dismissed</c>).</summary>
    ValueTask<RollbackDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken);
}

/// <summary>One daily evaluation of a champion to store (insert only).</summary>
/// <param name="ModelId">The model.</param>
/// <param name="SettingsHash">The SHA-256 of the thresholds.</param>
/// <param name="Verdicts">The two trigger readings as JSON.</param>
public sealed record NewMlMonitorCheck(long ModelId, string SettingsHash, string Verdicts);

/// <summary>The daily evaluations of the ML monitor (<c>research.ml_monitor_checks</c>): one per model and India day.</summary>
public interface IMlMonitorChecks
{
    /// <summary>Whether the model was already evaluated on this India day.</summary>
    ValueTask<bool> ExistsAsync(long modelId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Stores the evaluation; false when one exists for the day.</summary>
    ValueTask<bool> InsertAsync(NewMlMonitorCheck check, DateOnly day, CancellationToken cancellationToken);
}

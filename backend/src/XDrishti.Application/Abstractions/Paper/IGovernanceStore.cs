using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>What the configuration reading found: the flat components and the groups that could not be read (spec P10-05).</summary>
/// <param name="Components">The components that were read.</param>
/// <param name="Unreadable">Names of groups whose port failed; a non-empty list makes the snapshot partial.</param>
public sealed record ConfigReading(IReadOnlyList<ConfigComponent> Components, IReadOnlyList<string> Unreadable);

/// <summary>Reads the configuration of the process through the existing ports, once, without replay (spec P10-05). Writes nothing.</summary>
public interface IConfigSnapshotSource
{
    /// <summary>The components now in force. A port that fails is named in <see cref="ConfigReading.Unreadable"/>, never thrown.</summary>
    ValueTask<ConfigReading> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The flattened <c>Planning:*</c> configuration section (keys lower-cased, <c>:</c> as the separator). Secrets are not in this section.</summary>
public interface IPlanningSettingsReader
{
    /// <summary>Every set value under <c>Planning</c>.</summary>
    IReadOnlyDictionary<string, string> Read();
}

/// <summary>A stored snapshot (<c>paper.config_snapshots</c>).</summary>
/// <param name="Id">The id.</param>
/// <param name="TakenAt">When.</param>
/// <param name="SessionDate">The newest session of the capture run.</param>
/// <param name="Hash">The fingerprint.</param>
/// <param name="ComponentsJson">The canonical components.</param>
/// <param name="Partial">Whether a component could not be read.</param>
public sealed record StoredSnapshot(long Id, DateTimeOffset TakenAt, DateOnly SessionDate, string Hash, string ComponentsJson, bool Partial);

/// <summary>A snapshot to store.</summary>
/// <param name="TakenAt">When.</param>
/// <param name="SessionDate">The newest session of the run.</param>
/// <param name="Hash">The fingerprint.</param>
/// <param name="ComponentsJson">The canonical components.</param>
/// <param name="Partial">Whether a component could not be read.</param>
/// <param name="Process">The process that read it.</param>
public sealed record NewSnapshot(DateTimeOffset TakenAt, DateOnly SessionDate, string Hash, string ComponentsJson, bool Partial, string Process);

/// <summary>One difference to store.</summary>
/// <param name="Key">The key.</param>
/// <param name="Class">frozen, risk_down or free.</param>
/// <param name="Before">Earlier value, or null.</param>
/// <param name="After">Later value, or null.</param>
/// <param name="Effect">new_epoch or noted.</param>
/// <param name="ExplainedBy">hypothesis:id, changelog:id or null.</param>
public sealed record NewChange(string Key, string Class, string? Before, string? After, string Effect, string? ExplainedBy);

/// <summary>An epoch started by a detected change.</summary>
/// <param name="StartedOn">The first session of the epoch.</param>
/// <param name="At">When recorded.</param>
/// <param name="Hash">The new fingerprint.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record NewDetectedEpoch(DateOnly StartedOn, DateTimeOffset At, string Hash, string Reason);

/// <summary>What one capture run stores about the configuration, in one transaction.</summary>
/// <param name="Snapshot">The snapshot.</param>
/// <param name="FromSnapshotId">The previous snapshot, or null.</param>
/// <param name="NewEpoch">The epoch to start, or null.</param>
/// <param name="CurrentEpochId">The epoch in force before the run.</param>
/// <param name="Changes">The differences.</param>
public sealed record GovernanceCommit(NewSnapshot Snapshot, long? FromSnapshotId, NewDetectedEpoch? NewEpoch, long CurrentEpochId, IReadOnlyList<NewChange> Changes);

/// <summary>What a commit stored.</summary>
/// <param name="SnapshotId">The snapshot (new or the one stored already for the day and hash).</param>
/// <param name="EpochId">The epoch the changes belong to.</param>
public sealed record GovernanceCommitted(long SnapshotId, long EpochId);

/// <summary>A stored difference.</summary>
/// <param name="Id">The id.</param>
/// <param name="FromSnapshotId">Earlier snapshot.</param>
/// <param name="ToSnapshotId">Later snapshot.</param>
/// <param name="Key">The key.</param>
/// <param name="Class">frozen, risk_down or free.</param>
/// <param name="Before">Earlier value.</param>
/// <param name="After">Later value.</param>
/// <param name="Effect">new_epoch or noted.</param>
/// <param name="EpochId">The epoch it led to or belongs to.</param>
/// <param name="ExplainedBy">hypothesis:id, changelog:id or null.</param>
public sealed record StoredChange(long Id, long FromSnapshotId, long ToSnapshotId, string Key, string Class, string? Before, string? After, string Effect, long EpochId, string? ExplainedBy);

/// <summary>An epoch with its counts.</summary>
/// <param name="Epoch">The epoch row.</param>
/// <param name="Hash">The fingerprint of the epoch, or null.</param>
/// <param name="Counted">Entries that count.</param>
/// <param name="Straddle">Entries set aside.</param>
/// <param name="RiskDownChanges">Risk-down changes noted in the epoch.</param>
/// <param name="UnexplainedChanges">Changes with no explanation and no note.</param>
public sealed record EpochSummary(EpochRow Epoch, string? Hash, int Counted, int Straddle, int RiskDownChanges, int UnexplainedChanges);

/// <summary>An owner's note (<c>paper.epoch_notes</c>).</summary>
/// <param name="Id">The id.</param>
/// <param name="EpochId">The epoch.</param>
/// <param name="ChangeId">The change it explains, or null.</param>
/// <param name="HypothesisId">The hypothesis it names, or null.</param>
/// <param name="Text">The words.</param>
/// <param name="Actor">Who.</param>
/// <param name="CreatedAt">When.</param>
public sealed record EpochNote(long Id, long EpochId, long? ChangeId, long? HypothesisId, string Text, string Actor, DateTimeOffset CreatedAt);

/// <summary>An epoch started by the owner.</summary>
/// <param name="StartedOn">The first session of the epoch.</param>
/// <param name="At">When.</param>
/// <param name="Hash">The configuration hash in force, or null.</param>
/// <param name="Reason">The owner's reason.</param>
/// <param name="Actor">Who.</param>
/// <param name="PrevEpochId">The newest epoch the owner saw.</param>
public sealed record NewOwnerEpoch(DateOnly StartedOn, DateTimeOffset At, string? Hash, string Reason, string Actor, long PrevEpochId);

/// <summary>
/// The configuration record of the paper count (<c>paper.config_snapshots</c>, <c>config_changes</c>, <c>epoch_notes</c> and the epoch rows, spec P10-05, ADR 0119). Insert-only: no member updates or deletes.
/// </summary>
public interface IGovernanceStore
{
    /// <summary>The newest snapshot, or the newest full one.</summary>
    ValueTask<StoredSnapshot?> LatestSnapshotAsync(bool fullOnly, CancellationToken cancellationToken);

    /// <summary>Stores the snapshot, the new epoch and the changes in one transaction.</summary>
    ValueTask<GovernanceCommitted> CommitAsync(GovernanceCommit commit, CancellationToken cancellationToken);

    /// <summary>Stores the first epoch when there is none (with the hash of the configuration) and returns the newest epoch.</summary>
    ValueTask<EpochRow> CreateFirstEpochAsync(DateOnly startedOn, DateTimeOffset at, string? hash, CancellationToken cancellationToken);

    /// <summary>Stores an owner epoch when the newest epoch is still <see cref="NewOwnerEpoch.PrevEpochId"/>; null otherwise.</summary>
    ValueTask<EpochRow?> StartOwnerEpochAsync(NewOwnerEpoch epoch, CancellationToken cancellationToken);

    /// <summary>The epochs, newest first, with counts.</summary>
    ValueTask<IReadOnlyList<EpochSummary>> ListEpochsAsync(CancellationToken cancellationToken);

    /// <summary>The differences of one epoch (or all), newest first.</summary>
    ValueTask<IReadOnlyList<StoredChange>> ListChangesAsync(long? epochId, int limit, CancellationToken cancellationToken);

    /// <summary>One difference.</summary>
    ValueTask<StoredChange?> FindChangeAsync(long id, CancellationToken cancellationToken);

    /// <summary>The notes of one epoch (or all), newest first.</summary>
    ValueTask<IReadOnlyList<EpochNote>> ListNotesAsync(long? epochId, CancellationToken cancellationToken);

    /// <summary>Stores a note.</summary>
    ValueTask AddNoteAsync(long epochId, long? changeId, long? hypothesisId, string text, string actor, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Ids of hypotheses already named by a change or a note.</summary>
    ValueTask<IReadOnlySet<long>> UsedHypothesisIdsAsync(CancellationToken cancellationToken);
}

using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.Abstractions.LearnedSettings;

/// <summary>One row of <c>config.learned_setting_changes</c> (AU-10, ADR 0242): a change of one setting that learning proposed (or an owner undo), with who decided it.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Key">The setting key.</param>
/// <param name="OldValue">The value before, as invariant text.</param>
/// <param name="NewValue">The value after, as invariant text.</param>
/// <param name="Direction"><c>tighten</c> or <c>loosen</c>, decided from the descriptor when the row was made.</param>
/// <param name="Source"><c>learning</c> (the weekly run) or <c>owner</c> (an undo).</param>
/// <param name="State"><c>proposed</c>, <c>applied</c>, <c>dismissed</c> or <c>undone</c>.</param>
/// <param name="DecidedBy"><c>auto</c> or <c>owner:&lt;e-mail&gt;</c>; null while proposed.</param>
/// <param name="DecidedAt">When it was decided.</param>
/// <param name="UndoneBy">The owner who undid it.</param>
/// <param name="UndoneAt">When.</param>
/// <param name="SnapshotHash">The hash of the settings snapshot at proposal.</param>
/// <param name="Snapshot">The settings snapshot as JSON text (the Auto switch, the cap and the values of the allow-listed keys).</param>
/// <param name="EvidenceRef">What the change rests on, in words and ids.</param>
/// <param name="EvidenceHash">The hash of the evidence (a change is made once per evidence).</param>
/// <param name="UndoOf">The row this one undoes, if any.</param>
/// <param name="CreatedAt">When the row was made.</param>
public sealed record LearnedChangeRow(
    long Id,
    string Key,
    string OldValue,
    string NewValue,
    string Direction,
    string Source,
    string State,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? UndoneBy,
    DateTimeOffset? UndoneAt,
    string SnapshotHash,
    string Snapshot,
    string EvidenceRef,
    string EvidenceHash,
    long? UndoOf,
    DateTimeOffset CreatedAt);

/// <summary>A change to insert as <c>proposed</c>.</summary>
public sealed record NewLearnedChange(string Key, string OldValue, string NewValue, string Direction, string Source, string Snapshot, string SnapshotHash, string EvidenceRef, string EvidenceHash, long? UndoOf);

/// <summary>The change log behind a port (AU-10): insert-only, plus the move of the state column. Nothing places an order.</summary>
public interface ILearnedSettingStore
{
    /// <summary>The newest rows first.</summary>
    ValueTask<IReadOnlyList<LearnedChangeRow>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>One row, or null.</summary>
    ValueTask<LearnedChangeRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>For every key the new value of its newest <c>applied</c> row.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> AppliedValuesAsync(CancellationToken cancellationToken);

    /// <summary>How many rows <c>auto</c> applied since <paramref name="since"/>.</summary>
    ValueTask<int> AutoAppliedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Inserts a proposal; null when a row for this key and evidence exists already (idempotent).</summary>
    ValueTask<long?> InsertProposedAsync(NewLearnedChange change, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a <c>proposed</c> row, atomically per key: under a lock on the key it re-reads the newest applied value of the key and applies only if that is still <paramref name="expectedApplied"/> (null: no applied row). False when the row is not proposed any more, another decision
    /// for the key won, or the database refuses (for <c>auto</c> it refuses unless the Auto switch is ON).
    /// </summary>
    ValueTask<bool> ApplyAsync(long id, string decidedBy, string? expectedApplied, CancellationToken cancellationToken);

    /// <summary>Dismisses a <c>proposed</c> row; false when it is not proposed any more.</summary>
    ValueTask<bool> DismissAsync(long id, string decidedBy, CancellationToken cancellationToken);

    /// <summary>One transaction: the <paramref name="undo"/> row is inserted and applied by <paramref name="owner"/>, and row <paramref name="id"/> becomes <c>undone</c>. Null when row <paramref name="id"/> is not the NEWEST applied row of its key any more (checked under the same lock as <see cref="ApplyAsync"/>).</summary>
    ValueTask<long?> UndoAsync(long id, NewLearnedChange undo, string owner, CancellationToken cancellationToken);
}

/// <summary>The evidence of one learning proposal for one allow-listed key.</summary>
/// <param name="Key">The setting.</param>
/// <param name="ProposedValue">The value learning would like, as invariant text.</param>
/// <param name="Reference">What it rests on, in words and ids.</param>
/// <param name="Hash">The hash of the evidence.</param>
/// <param name="OutOfSample">True when the proof is out of sample.</param>
/// <param name="FromSealedHoldOut">True when the figures came from sealed hold-out data (never acceptable here).</param>
public sealed record LearnedEvidenceItem(string Key, string ProposedValue, string Reference, string Hash, bool OutOfSample, bool FromSealedHoldOut);

/// <summary>What the learning services can say this week: <c>Available</c> false means unavailable (never a pass).</summary>
public sealed record LearnedEvidenceBatch(bool Available, IReadOnlyList<LearnedEvidenceItem> Items)
{
    public static LearnedEvidenceBatch Unavailable { get; } = new(false, []);
}

/// <summary>The source of learned evidence. The only implementation today reads nothing (unavailable): a later slice adds the measured source behind this port.</summary>
public interface ILearnedEvidenceSource
{
    ValueTask<LearnedEvidenceBatch> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The running configuration as the Api host knows it: descriptors, the value in force, and the whole-configuration check (AU-10 criterion 10).</summary>
public interface ILearnedConfig
{
    /// <summary>The descriptor of a key, or null.</summary>
    SettingDescriptor? Descriptor(string key);

    /// <summary>The configured value (before any learned change) as invariant text, or null for an unknown key.</summary>
    string? ConfiguredValue(string key);

    /// <summary>Validates the WHOLE candidate configuration (the configured values with <paramref name="overrides"/> laid over them) with every existing validator and the descriptor bounds. Empty when it is valid.</summary>
    IReadOnlyList<string> Failures(IReadOnlyDictionary<string, string> overrides);
}

/// <summary>Whether the owner switched Auto on (<c>auto.enabled</c>). Unreadable or missing is OFF.</summary>
public interface IAutoSwitch
{
    ValueTask<bool> IsOnAsync(CancellationToken cancellationToken);
}

/// <summary>How many sessions in a row the coming plan was empty (AU-10 criterion 9), or null when that cannot be known.</summary>
public interface IEmptyPlanProbe
{
    ValueTask<int?> ConsecutiveEmptySessionsAsync(CancellationToken cancellationToken);
}

/// <summary>An applied learned value as the host reads it at start.</summary>
/// <param name="Key">The setting.</param>
/// <param name="Value">The value as invariant text.</param>
/// <param name="DecidedBy"><c>auto</c> or <c>owner:&lt;e-mail&gt;</c>.</param>
public sealed record LearnedOverride(string Key, string Value, string DecidedBy);

/// <summary>
/// The applied learned values, read once while the host builds its options (so it is synchronous, by design). Never throws: when the log cannot be read the answer is <c>null</c> and the host starts with the configured values; a learned change must never make the host refuse to start.
/// </summary>
public interface ILearnedOverrideSource
{
    /// <summary>The newest applied value of every key, or null when the log could not be read.</summary>
    IReadOnlyList<LearnedOverride>? ReadApplied();
}

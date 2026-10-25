using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Paper;

/// <summary>One epoch as the API shows it (spec P10-05). Epochs are never pooled: each carries its own count.</summary>
/// <param name="Id">The id.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="StartedOn">The first session of the epoch.</param>
/// <param name="EndedOn">The next epoch's start, or null for the epoch in force.</param>
/// <param name="Fingerprint">The first 12 characters of the configuration hash, or null.</param>
/// <param name="Counted">Entries that count.</param>
/// <param name="Straddle">Entries set aside around a change (stored, shown, never counted).</param>
/// <param name="RiskDownChanges">Risk-down changes noted in the epoch.</param>
/// <param name="UnexplainedChanges">Changes with neither an explanation nor a note.</param>
/// <param name="Reason">Why the epoch started.</param>
public sealed record EpochView(long Id, string Kind, DateOnly StartedOn, DateOnly? EndedOn, string? Fingerprint, int Counted, int Straddle, int RiskDownChanges, int UnexplainedChanges, string Reason);

/// <summary>One stored difference of the configuration.</summary>
/// <param name="Id">The id.</param>
/// <param name="EpochId">The epoch it led to or belongs to.</param>
/// <param name="Key">The key.</param>
/// <param name="Class">frozen, risk_down or free.</param>
/// <param name="Before">Earlier value; null when added.</param>
/// <param name="After">Later value; null when removed.</param>
/// <param name="Effect">new_epoch or noted.</param>
/// <param name="ExplainedBy">hypothesis:id, changelog:id or null.</param>
/// <param name="Unexplained">True when there is no explanation and no owner note.</param>
public sealed record ConfigChangeView(long Id, long EpochId, string Key, string Class, string? Before, string? After, string Effect, string? ExplainedBy, bool Unexplained);

/// <summary>An owner's note on an epoch.</summary>
/// <param name="Id">The id.</param>
/// <param name="ChangeId">The change it explains, or null.</param>
/// <param name="HypothesisId">The hypothesis it names, or null.</param>
/// <param name="Text">The words.</param>
/// <param name="Actor">Who.</param>
/// <param name="CreatedAt">When.</param>
public sealed record EpochNoteView(long Id, long? ChangeId, long? HypothesisId, string Text, string Actor, DateTimeOffset CreatedAt);

/// <summary>The epochs, newest first.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Items">The epochs.</param>
public sealed record EpochList(string Basis, IReadOnlyList<EpochView> Items);

/// <summary>One epoch with its changes and notes.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Epoch">The epoch.</param>
/// <param name="Changes">The differences recorded under it.</param>
/// <param name="Notes">The owner's notes.</param>
public sealed record EpochDetail(string Basis, EpochView Epoch, IReadOnlyList<ConfigChangeView> Changes, IReadOnlyList<EpochNoteView> Notes);

/// <summary>The recorded differences.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Items">The differences, newest first.</param>
public sealed record ConfigChangeList(string Basis, IReadOnlyList<ConfigChangeView> Items);

/// <summary>One component of the configuration with the rule that classes a change of it.</summary>
/// <param name="Key">The key.</param>
/// <param name="Rule">What a change of it does to the count.</param>
/// <param name="Value">The value in force now.</param>
public sealed record ConfigComponentView(string Key, string Rule, string Value);

/// <summary>A difference the next capture would see.</summary>
/// <param name="Key">The key.</param>
/// <param name="Class">frozen, risk_down or free.</param>
/// <param name="Before">The value in the last snapshot.</param>
/// <param name="After">The value now.</param>
/// <param name="Meaning">would restart the count, or noted only.</param>
public sealed record PendingChangeView(string Key, string Class, string? Before, string? After, string Meaning);

/// <summary>A service that would restart the count when it runs.</summary>
/// <param name="ServiceId">The service id.</param>
/// <param name="Text">The warning.</param>
public sealed record BlockerView(string ServiceId, string Text);

/// <summary>The last snapshot taken.</summary>
/// <param name="Hash">The first 12 characters of its hash.</param>
/// <param name="TakenAt">When.</param>
/// <param name="SessionDate">The newest session of the run.</param>
public sealed record LastSnapshotView(string Hash, DateTimeOffset TakenAt, DateOnly SessionDate);

/// <summary>The configuration now, its class rules, the last snapshot and the differences not yet captured (spec P10-05 criteria 10 and 11a). A preview: nothing is stored.</summary>
/// <param name="Basis">The label.</param>
/// <param name="CurrentHash">The first 12 characters of the hash of the configuration now.</param>
/// <param name="Last">The last full snapshot, or null.</param>
/// <param name="Components">Every component.</param>
/// <param name="Pending">Differences from the last snapshot.</param>
/// <param name="Blockers">Enabled services that restart the count.</param>
/// <param name="Unreadable">Groups that could not be read.</param>
public sealed record ConfigView(string Basis, string CurrentHash, LastSnapshotView? Last, IReadOnlyList<ConfigComponentView> Components, IReadOnlyList<PendingChangeView> Pending, IReadOnlyList<BlockerView> Blockers, IReadOnlyList<string> Unreadable);

/// <summary>An accepted hypothesis waiting; nothing was applied.</summary>
/// <param name="HypothesisId">The id.</param>
/// <param name="AgeDays">Days since it was accepted.</param>
/// <param name="CellKey">The cell, or null.</param>
/// <param name="Knob">The knob.</param>
/// <param name="From">The baseline value.</param>
/// <param name="To">The candidate value.</param>
/// <param name="ExperimentId">The stored experiment, or null.</param>
public sealed record QueueItem(long HypothesisId, int AgeDays, string? CellKey, string Knob, string From, string To, long? ExperimentId);

/// <summary>The queue of accepted hypotheses that no change names.</summary>
/// <param name="Note">Accepted means judged better offline; nothing was applied; applying it starts a new epoch.</param>
/// <param name="Items">The hypotheses, oldest first.</param>
public sealed record QueueView(string Note, IReadOnlyList<QueueItem> Items);

/// <summary>What the explain verb stored.</summary>
/// <param name="ChangeId">The change.</param>
/// <param name="EpochId">The epoch.</param>
public sealed record ExplainOutcome(long ChangeId, long EpochId);

/// <summary>The epochs, newest first (spec P10-05). Read only.</summary>
public sealed record GetEpochsQuery : IQuery<Result<EpochList>>;

/// <summary>One epoch with its changes and notes.</summary>
/// <param name="Id">The epoch id.</param>
public sealed record GetEpochQuery(long Id) : IQuery<Result<EpochDetail>>;

/// <summary>The recorded differences of one epoch, or all.</summary>
/// <param name="EpochId">One epoch, or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record GetConfigChangesQuery(long? EpochId, int Limit) : IQuery<Result<ConfigChangeList>>;

/// <summary>The configuration now with its classes and differences (preview, stores nothing).</summary>
public sealed record GetConfigQuery : IQuery<Result<ConfigView>>;

/// <summary>The queue of accepted hypotheses nothing has used.</summary>
public sealed record GetQueueQuery : IQuery<Result<QueueView>>;

/// <summary>The owner restarts the count from the next trading day (CLI only).</summary>
/// <param name="Reason">10 to 300 characters.</param>
/// <param name="Actor">Who.</param>
public sealed record StartEpochCommand(string? Reason, string Actor) : ICommand;

/// <summary>The owner attaches a note (and optionally an accepted hypothesis) to a recorded change (CLI only).</summary>
/// <param name="ChangeId">The change.</param>
/// <param name="HypothesisId">The hypothesis it came from, or null.</param>
/// <param name="Reason">10 to 300 characters.</param>
/// <param name="Actor">Who.</param>
public sealed record ExplainChangeCommand(long ChangeId, long? HypothesisId, string? Reason, string Actor) : ICommand;

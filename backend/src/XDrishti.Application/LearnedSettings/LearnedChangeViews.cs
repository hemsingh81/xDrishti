using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.LearnedSettings;

/// <summary>The learned changes: the log with the guardrails and the allow-list.</summary>
public sealed record GetLearnedChangesQuery : IQuery<LearnedChangesView>;

/// <summary>One change by id.</summary>
public sealed record GetLearnedChangeQuery(long Id) : IQuery<LearnedChangeView?>;

/// <summary>
/// <c>AutoOn</c>: the Auto switch (OFF by default). <c>WeeklyCap</c> and <c>AutoAppliedThisWeek</c>: the guardrail and what it has used in the trailing 7 days. <c>TakesEffect</c>: when an applied change is read by the host. <c>Allowlist</c>: the keys learning may touch.
/// </summary>
public sealed record LearnedChangesView(bool AutoOn, int WeeklyCap, int AutoAppliedThisWeek, string TakesEffect, IReadOnlyList<LearnedAllowedSetting> Allowlist, IReadOnlyList<LearnedChangeView> Items);

/// <summary>An allow-listed key with its label, its safer direction and the bounds.</summary>
public sealed record LearnedAllowedSetting(string Key, string Label, string Safer, SettingRange? Range);

/// <summary>
/// One change. <c>Direction</c> is tighten or loosen. <c>State</c> is proposed, applied, dismissed or undone. <c>DecidedBy</c> is <c>auto</c> (never shown as an owner approval) or <c>owner:&lt;e-mail&gt;</c>. <c>ApproveNeedsPassword</c> is true for a loosening;
/// <c>CanUndo</c> is true for an applied change and <c>UndoNeedsPassword</c> says whether the undo loosens (undoing a tightening always does).
/// </summary>
public sealed record LearnedChangeView(
    long Id,
    string Key,
    string Label,
    string OldValue,
    string NewValue,
    string Direction,
    string Source,
    string State,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? UndoneBy,
    DateTimeOffset? UndoneAt,
    string EvidenceRef,
    string SnapshotHash,
    long? UndoOf,
    DateTimeOffset CreatedAt,
    bool ApproveNeedsPassword,
    bool CanUndo,
    bool UndoNeedsPassword);

/// <summary>Approve (apply) a proposed change as the owner. <c>FreshPassword</c> is true only after the endpoint verified a fresh password for this act.</summary>
public sealed record ApproveLearnedChangeCommand(long Id, string Actor, bool FreshPassword) : ICommand;

/// <summary>Dismiss a proposed change as the owner.</summary>
public sealed record DismissLearnedChangeCommand(long Id, string Actor) : ICommand;

/// <summary>Undo an applied change as the owner (one step). <c>FreshPassword</c> as above.</summary>
public sealed record UndoLearnedChangeCommand(long Id, string Actor, bool FreshPassword) : ICommand;

/// <summary>Run learning over this week's evidence.</summary>
public sealed record RunLearnedSettingsCommand(string Requester) : ICommand;

/// <summary><c>Outcome</c> is a sentence; <c>Skipped</c> lists the keys left alone with the code.</summary>
public sealed record LearnedRunView(string Outcome, int Proposed, int Applied, IReadOnlyList<LearnedSkip> Skipped);

public sealed record LearnedSkip(string Key, string Code);

internal static class LearnedViews
{
    /// <summary>A row of the log. <paramref name="isCurrent"/> is true for the newest applied row of its key: only that one can be undone (an older one is replaced).</summary>
    public static LearnedChangeView Of(LearnedChangeRow r, ILearnedConfig config, bool isCurrent)
    {
        var descriptor = config.Descriptor(r.Key);
        return new LearnedChangeView(
            r.Id, r.Key, descriptor?.Label ?? r.Key, r.OldValue, r.NewValue, r.Direction, r.Source, r.State, r.DecidedBy, r.DecidedAt, r.UndoneBy, r.UndoneAt, r.EvidenceRef, r.SnapshotHash, r.UndoOf, r.CreatedAt,
            LearnedRules.ApproveNeedsPassword(r, descriptor), r.State == "applied" && isCurrent, LearnedRules.UndoNeedsPassword(r, descriptor));
    }
}

using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Halts;

/// <summary>A halt to record. The source is <c>owner</c> (the actor starts <c>owner:</c>) or <c>system</c> (it starts <c>system:</c>).</summary>
public sealed record HaltDraft(HaltScope Scope, Guid? AccountId, HaltLevel Level, string TriggerCode, string Source, string Actor, string Reason, DateTimeOffset At, bool BrokerKillSwitch = false);

/// <summary>
/// The insert-only halt record. A halt is active while it has neither an owner release (written by xd-api) nor an automatic clear (written here). <see cref="ActiveAsync"/> throws when the record cannot be read: the caller treats that as halted.
/// There is no method that removes, edits or releases a halt, by design.
/// </summary>
public interface IHaltStore
{
    /// <summary>Records the halt, or returns the one already active for the same trigger, scope and account at this level or a stronger one (a repeat never stacks). A stronger level than the active one is a new row.</summary>
    Task<(ActiveHalt Halt, bool Created)> AppendAsync(HaltDraft draft, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActiveHalt>> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>Writes the automatic clear of an active halt whose trigger may clear itself; false when it was not active or the trigger is an owner-only one.</summary>
    Task<bool> AutoClearAsync(long haltId, string triggerCode, string detail, DateTimeOffset at, CancellationToken cancellationToken);
}

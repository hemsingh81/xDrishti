using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// The kill switch's one service (P12-05): it records halts (<see cref="IHaltRequests"/>, which can only ask for MORE caution) and answers the gate (<see cref="IHaltSource"/>). A halt of any kind or scope reads as <see cref="HaltReading.Halted"/> (the entry
/// gate does not look at accounts: stricter, never looser), an unreadable record reads as <see cref="HaltReading.Unreadable"/> and both block entries. Nothing in this class releases or clears a halt: the owner's release is written by xd-api, the
/// automatic clears by <see cref="HaltAutoClearService"/> for the codes that may clear themselves.
/// </summary>
public sealed class HaltService(IHaltStore store, TimeProvider clock) : IHaltRequests, IHaltSource
{
    public const string SystemActor = "system:halts";

    public async Task RequestAsync(Guid? accountId, HaltLevel level, string triggerCode, string detail, CancellationToken cancellationToken)
    {
        var trigger = HaltTriggers.Find(triggerCode) ?? throw new ArgumentException("Unknown trigger code.", nameof(triggerCode));
        if (triggerCode == HaltTriggers.OwnerButton)
        {
            throw new ArgumentException("The owner's button is an owner act; use RequestAsOwnerAsync.", nameof(triggerCode));
        }

        var scope = accountId is null ? HaltScope.System : HaltScope.Account;

        var asked = level > trigger.DefaultLevel ? level : trigger.DefaultLevel;
        if ((asked == HaltLevel.ExitAll && triggerCode is not (HaltTriggers.LossCap or HaltTriggers.DisasterLoss))
            || (asked == HaltLevel.Freeze && triggerCode is not (HaltTriggers.ReconciliationBreak or HaltTriggers.Oversold)))
        {
            throw new ArgumentException("This trigger may not ask for that level.", nameof(level));
        }

        // A request below the trigger's own level is raised to it: a trigger never asks for less than its table row, and nothing here asks for less than it was told.
        await store.AppendAsync(new HaltDraft(scope, accountId, asked, triggerCode, "system", SystemActor, Trim(detail), clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The owner's halt (the button). The actor was built from the signed-in session by xd-api and arrives in the command row; it must start <c>owner:</c>.</summary>
    public async Task<ActiveHalt> RequestAsOwnerAsync(Guid? accountId, HaltLevel level, string reason, string actor, bool brokerKillSwitch, CancellationToken cancellationToken)
    {
        if (!actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length)
        {
            throw new ArgumentException("An owner halt needs an owner actor.", nameof(actor));
        }

        var scope = accountId is null ? HaltScope.System : HaltScope.Account;
        return (await store.AppendAsync(new HaltDraft(scope, accountId, level, HaltTriggers.OwnerButton, "owner", actor, Trim(reason), clock.GetUtcNow(), brokerKillSwitch), cancellationToken).ConfigureAwait(false)).Halt;
    }

    public async Task<HaltReading> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await store.ActiveAsync(cancellationToken).ConfigureAwait(false)).Count == 0 ? HaltReading.Clear : HaltReading.Halted;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return HaltReading.Unreadable;
        }
    }

    /// <summary>The halts in force, or null when the record cannot be read (callers treat null as halted).</summary>
    public async Task<IReadOnlyList<ActiveHalt>?> ActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.ActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string Trim(string text) => text.Length > 300 ? text[..300] : text;
}

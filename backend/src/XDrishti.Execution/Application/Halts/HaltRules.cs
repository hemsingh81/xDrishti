using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Halts;

/// <summary>What an instruction wants to do, for the permission table of P12-05.</summary>
public enum HaltAction
{
    NewEntry,
    CancelWorkingEntry,
    PlaceOrRepairStop,
    TightenStop,
    Exit,
}

public enum HaltScope
{
    System,
    Account,
}

/// <summary>One halt in force: it has neither an owner release nor an automatic clear.</summary>
public sealed record ActiveHalt(long Id, DateTimeOffset At, HaltScope Scope, Guid? AccountId, HaltLevel Level, string TriggerCode, string Source, string Actor, string Reason, bool BrokerKillSwitch = false);

/// <summary>
/// The permission table (spec P12-05, 25 cells) and the effective state of an account: the strongest active halt of the system and of the account (freeze, exit_all, cancel, entries; null = none). A halt can only take permissions away, never add one: a
/// null state permits everything, every level permits a subset of the level before it, except that <c>freeze</c> stops tightening and cancelling and still lets a protective stop be placed and a position be flattened, each guarded by a fresh broker read.
/// </summary>
public static class HaltRules
{
    public static bool Permits(HaltLevel? state, HaltAction action) => state switch
    {
        null => true,
        HaltLevel.Entries => action != HaltAction.NewEntry,
        HaltLevel.Cancel => action != HaltAction.NewEntry,
        HaltLevel.ExitAll => action is HaltAction.CancelWorkingEntry or HaltAction.PlaceOrRepairStop or HaltAction.Exit,
        HaltLevel.Freeze => action is HaltAction.PlaceOrRepairStop or HaltAction.Exit,
        _ => false, // an unknown level permits nothing
    };

    /// <summary>True when the permitted action must be guarded by a fresh broker position read that agrees with our quantity (P12-04 Authority): only under freeze.</summary>
    public static bool NeedsFreshBrokerRead(HaltLevel? state, HaltAction action) => state == HaltLevel.Freeze && Permits(state, action);

    public static HaltLevel? Effective(IEnumerable<ActiveHalt> active, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(active);
        HaltLevel? strongest = null;
        foreach (var halt in active)
        {
            if ((halt.Scope == HaltScope.System || halt.AccountId == accountId) && (strongest is null || halt.Level > strongest))
            {
                strongest = halt.Level;
            }
        }

        return strongest;
    }
}

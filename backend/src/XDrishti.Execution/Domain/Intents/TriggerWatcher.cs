namespace XDrishti.Execution.Domain.Intents;

/// <summary>
/// The crossing test of a ticket's trigger level (spec P12-07 scope, reused by P12-08's intent creator). Pure: a BUY trigger is reached when the last price is at or above it, a SELL trigger when it is at or below it. A level or price that
/// is not positive is never a crossing.
/// </summary>
public static class TriggerWatcher
{
    public static bool TriggerCrossed(ExecSide side, decimal trigger, decimal last) =>
        trigger > 0 && last > 0 && (side == ExecSide.Buy ? last >= trigger : last <= trigger);

    /// <summary>
    /// Whether the price has already run past the trigger by more than the collar, measured as P12-03's <c>ltp_collar</c> measures it (the distance as a percentage of the last price). Such a trigger is void: the entry could only be chased.
    /// </summary>
    public static bool PassedBeyond(ExecSide side, decimal trigger, decimal last, decimal collarPct) =>
        TriggerCrossed(side, trigger, last) && Math.Abs(trigger - last) / last * 100m > collarPct;
}

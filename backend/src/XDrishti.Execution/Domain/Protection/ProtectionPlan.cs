namespace XDrishti.Execution.Domain.Protection;

/// <summary>A protective sell stop: quantity, trigger and the limit under it.</summary>
public sealed record StopInstruction(int Qty, decimal Trigger, decimal Limit);

/// <summary>The entry as the structure sends it: <c>super</c> carries the stop and the target, <c>plain_stop</c> neither.</summary>
public sealed record EntryInstruction(ExecStructure Structure, int Qty, decimal Price, decimal? StopLoss, decimal? Target);

/// <summary>Instructions for the protective structure (spec P12-04 criterion 1). Entries are BUY only: a SELL intent has no instruction, so there is no short entry and the stop, target and exit instructions are always sells.</summary>
public static class ProtectionPlan
{
    public static EntryInstruction? Entry(ExecIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return intent.Side != ExecSide.Buy
            ? null
            : intent.Structure == ExecStructure.Super
                ? new EntryInstruction(ExecStructure.Super, intent.Qty, intent.Entry, intent.Stop, intent.Target)
                : new EntryInstruction(ExecStructure.PlainStop, intent.Qty, intent.Entry, null, null);
    }

    /// <summary>The stop for <paramref name="filledQty"/> shares at <paramref name="trigger"/>; null when nothing is filled.</summary>
    public static StopInstruction? StopFor(int filledQty, decimal trigger, decimal bufferPct) =>
        filledQty <= 0 || trigger <= 0m ? null : new StopInstruction(filledQty, trigger, StopRules.StopLimit(trigger, bufferPct));
}

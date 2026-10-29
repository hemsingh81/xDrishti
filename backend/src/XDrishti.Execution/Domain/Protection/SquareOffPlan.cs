namespace XDrishti.Execution.Domain.Protection;

public enum SquareOffStepKind
{
    /// <summary>Nothing to do now.</summary>
    None,

    /// <summary>Cancel every working or part-filled entry (its filled part stays and is protected).</summary>
    CancelEntries,

    /// <summary>Cancel the target and wait for the acknowledgement before an exit is sent.</summary>
    CancelTarget,

    SendExit,

    /// <summary>The exit has filled and the position is flat: cancel the stop that is left.</summary>
    CancelStop,

    /// <summary>An entry is still working after the square-off time and the cancel was tried: CRITICAL <c>exit_failed</c>.</summary>
    ExitFailed,

    /// <summary>Flat, no stop, no entry: done.</summary>
    Done,
}

public sealed record SquareOffStep(SquareOffStepKind Kind, int Qty = 0);

public sealed record SquareOffState(TimeOnly NowIst, TimeOnly EntryWindowEnd, TimeOnly SquareOffTime, int WorkingEntries, bool EntryCancelTried, bool TargetLive, bool TargetCancelAcked, int NetQty, int ExitsInFlight, bool StopLive);

/// <summary>The order of the end-of-day work (spec P12-04): entries off at the entry window end and again at the square-off time, then target off, then a reduce-only exit, the stop only after the exit filled. Pure: one next step from the state.</summary>
public static class SquareOffPlan
{
    public static SquareOffStep Next(SquareOffState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var afterEntryWindow = s.NowIst >= s.EntryWindowEnd;
        var afterSquareOff = s.NowIst >= s.SquareOffTime;
        if (s.WorkingEntries > 0 && afterEntryWindow)
        {
            return afterSquareOff && s.EntryCancelTried ? new SquareOffStep(SquareOffStepKind.ExitFailed) : new SquareOffStep(SquareOffStepKind.CancelEntries);
        }

        if (!afterSquareOff)
        {
            return new SquareOffStep(SquareOffStepKind.None);
        }

        if (s.NetQty > 0)
        {
            if (s.TargetLive && !s.TargetCancelAcked)
            {
                return new SquareOffStep(SquareOffStepKind.CancelTarget);
            }

            var qty = StopRules.MaxExitQty(s.NetQty, s.ExitsInFlight);
            return qty > 0 ? new SquareOffStep(SquareOffStepKind.SendExit, qty) : new SquareOffStep(SquareOffStepKind.None);
        }

        return s.StopLive ? new SquareOffStep(SquareOffStepKind.CancelStop) : new SquareOffStep(SquareOffStepKind.Done);
    }
}

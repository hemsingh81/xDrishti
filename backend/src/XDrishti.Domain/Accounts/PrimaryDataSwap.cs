using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// Switching the primary Data account. The previous primary becomes the standby when the standby slot is free (or the new primary was
/// the standby: a true swap), otherwise it loses the Data role. Applied in two steps — <see cref="Release"/> then <see cref="Complete"/> —
/// so that, saved one after the other in one transaction, the database's uniqueness rules are never violated half-way.
/// </summary>
public sealed class PrimaryDataSwap
{
    private PrimaryDataSwap(BrokerAccount target, BrokerAccount? previous, bool previousBecomesStandby, bool isNoOp)
    {
        Target = target;
        Previous = previous;
        PreviousBecomesStandby = previousBecomesStandby;
        IsNoOp = isNoOp;
    }

    public BrokerAccount Target { get; }

    /// <summary>The account that was primary until now; null when there was none.</summary>
    public BrokerAccount? Previous { get; }

    public bool PreviousBecomesStandby { get; }

    /// <summary>The target already is the primary: nothing to do.</summary>
    public bool IsNoOp { get; }

    public static Result<PrimaryDataSwap> Plan(BrokerAccount target, BrokerAccount? currentPrimary, BrokerAccount? currentStandby)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ArchivedAt is not null)
        {
            return Result.Failure<PrimaryDataSwap>(AccountErrors.NotFound);
        }

        if (target.DataPrimary)
        {
            return Result.Success(new PrimaryDataSwap(target, null, false, true));
        }

        var previous = currentPrimary is not null && currentPrimary.Id != target.Id ? currentPrimary : null;
        var standbySlotFree = currentStandby is null || currentStandby.Id == target.Id;
        return Result.Success(new PrimaryDataSwap(target, previous, previous is not null && standbySlotFree, false));
    }

    /// <summary>Step 1: both accounts give up their primary/standby flags.</summary>
    public void Release(DateTimeOffset now)
    {
        Target.ClearDataKind(now);
        Previous?.ClearDataKind(now);
    }

    /// <summary>Step 2: the target becomes primary; the previous primary becomes standby or loses the Data role.</summary>
    public void Complete(DateTimeOffset now)
    {
        Target.SetDataKind(DataRoleKind.Primary, now);
        if (Previous is null)
        {
            return;
        }

        if (PreviousBecomesStandby)
        {
            Previous.SetDataKind(DataRoleKind.Standby, now);
        }
        else
        {
            Previous.DisableData(now);
        }
    }
}

using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Autonomy;

/// <summary>
/// A change of the autonomy settings (spec P7-12): the only way a new settings row comes to be. Only an owner actor can make one (<c>owner:...</c>, taken from the authenticated identity by the caller, never from a request), with a reason;
/// the values are checked. Every change is stored as a new row, so the history of the switches is an audit trail.
/// </summary>
public sealed record AutonomyChange
{
    /// <summary>The longest reason (the column is 500).</summary>
    public const int MaxReasonLength = 500;

    private AutonomyChange(string actor, string reason, bool autoApproveTickets, bool promoteFromReserve, double minPWin, int maxAutoPerDay)
    {
        Actor = actor;
        Reason = reason;
        AutoApproveTickets = autoApproveTickets;
        PromoteFromReserve = promoteFromReserve;
        MinPWin = minPWin;
        MaxAutoPerDay = maxAutoPerDay;
    }

    /// <summary>The owner who made it.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed.</summary>
    public string Reason { get; }

    /// <summary>The new value of <c>autonomy.auto_approve_tickets</c>.</summary>
    public bool AutoApproveTickets { get; }

    /// <summary>The new value of <c>autonomy.promote_from_reserve</c>.</summary>
    public bool PromoteFromReserve { get; }

    /// <summary>The new threshold.</summary>
    public double MinPWin { get; }

    /// <summary>The new daily cap.</summary>
    public int MaxAutoPerDay { get; }

    /// <summary>Makes the change, or says why not: an owner actor, a reason that fits and values in range.</summary>
    public static Result<AutonomyChange> Create(string? actor, string? reason, bool autoApproveTickets, bool promoteFromReserve, double minPWin, int maxAutoPerDay)
    {
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<AutonomyChange>(Error.Forbidden("autonomy.owner_only", "Only the signed-in owner changes the autonomy settings."));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            return Result.Failure<AutonomyChange>(Error.Validation("autonomy.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        if (!AutonomySettings.ValuesAreValid(minPWin, maxAutoPerDay))
        {
            return Result.Failure<AutonomyChange>(Error.Validation("autonomy.values_invalid", $"The threshold is {AutonomySettings.LowestMinPWin} to {AutonomySettings.HighestMinPWin} and the daily cap 1 to {AutonomySettings.HighestMaxAutoPerDay}."));
        }

        return Result.Success(new AutonomyChange(actor, text, autoApproveTickets, promoteFromReserve, minPWin, maxAutoPerDay));
    }
}

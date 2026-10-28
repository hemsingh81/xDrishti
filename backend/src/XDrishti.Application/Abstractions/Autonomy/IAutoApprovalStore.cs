using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.Abstractions.Autonomy;

/// <summary>
/// Writes an auto approval (spec P7-12, ADR 0091), separately from the owner's moves so that <see cref="IPlanStore.MoveItemAsync"/> stays as it was. One transaction: an approval row of kind auto (actor <c>rule:autonomy</c>, the settings snapshot, the
/// policy version) and the ticket's Proposed to Approved row. The database refuses it unless the settings row had the switch ON at that moment, the item is primary and the time is before the cutoff. Of an owner's decision and an auto approval
/// on one ticket, one wins. Nothing here places an order.
/// </summary>
public interface IAutoApprovalStore
{
    /// <summary>Writes the approval and the move. <see cref="MoveStatus.Created"/>, <see cref="MoveStatus.Stale"/> (another move won), <see cref="MoveStatus.NotFound"/>, <see cref="MoveStatus.AfterCutoff"/> or <see cref="MoveStatus.Refused"/> (plan not open, not primary, or the database refused: switch not on in that row, cap reached).</summary>
    ValueTask<MoveStatus> AutoApproveAsync(AutoApproveMove move, CancellationToken cancellationToken);

    /// <summary>How many auto approvals the plans of this planned session have.</summary>
    ValueTask<int> CountAutoApprovalsAsync(DateOnly forDate, CancellationToken cancellationToken);
}

/// <summary>An auto approval to write.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="SettingsId">The settings row the decision rests on.</param>
/// <param name="SettingsHash">Its hash.</param>
/// <param name="PolicyVersion">The version of the rule.</param>
/// <param name="Reason">The sentence stored (starts with the auto-approved marker).</param>
/// <param name="LiveSettingsId">For a ticket with a live allocation, the live settings row the decision also rests on (spec P11-04); null for a paper ticket. The database refuses a live ticket without it.</param>
/// <param name="LiveSettingsHash">Its hash.</param>
public sealed record AutoApproveMove(long ItemId, long SettingsId, string SettingsHash, string PolicyVersion, string Reason, long? LiveSettingsId = null, string? LiveSettingsHash = null);

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The owner's replacement of the evidence set of the current edge epoch (spec AM-01 GAP-09, ADR 0282). Refuses a non-owner actor, a missing or over-long reason, a set that is empty, too large, malformed or names a version that is not stored, and any replace once a shadow row of
/// the epoch exists (409 <c>paper.evidence_set_frozen</c>). Stores one row (never an update) and the ledger registration in one transaction, then one account event <c>EvidenceSetReplaced</c> whose failure is logged and does not undo the row. The endpoint is responsible for the
/// session, the CSRF token and the fresh password. It reads no shadow outcome and places no order.
/// </summary>
internal sealed class ReplaceEvidenceSetHandler(
    IEvidenceSetStore store,
    IPaperStore paper,
    IStrategyStore strategies,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReplaceEvidenceSetHandler> logger) : ICommandHandler<ReplaceEvidenceSetCommand, EvidenceSetReplaced>
{
    public const int MaxReasonLength = 500;

    public const string OwnerOnlyCode = "paper.evidence_set_owner_only";

    public const string ReasonInvalidCode = "paper.evidence_set_reason_invalid";

    public const string VersionsInvalidCode = "paper.evidence_set_versions_invalid";

    public const string UnknownVersionCode = "paper.evidence_set_unknown_version";

    public const string FrozenCode = "paper.evidence_set_frozen";

    public async ValueTask<Result<EvidenceSetReplaced>> HandleAsync(ReplaceEvidenceSetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            return Result.Failure<EvidenceSetReplaced>(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner replaces the evidence set."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<EvidenceSetReplaced>(Error.Validation(ReasonInvalidCode, "A replace needs a reason of 1 to 500 characters."));
        }

        if (EvidenceSetRules.Canonical(command.Versions) is not { } versions)
        {
            return Result.Failure<EvidenceSetReplaced>(Error.Validation(VersionsInvalidCode, $"A set holds 1 to {EvidenceSetRules.MaxVersions} distinct strategy version hashes (64 lower-case hex characters each)."));
        }

        foreach (var hash in versions)
        {
            if (await strategies.GetByHashAsync(hash, cancellationToken) is null)
            {
                return Result.Failure<EvidenceSetReplaced>(Error.Validation(UnknownVersionCode, "A version of the set is not a stored strategy version."));
            }
        }

        var epoch = (await paper.CurrentEpochAsync(cancellationToken))?.Id;
        var key = EvidenceSetRules.EpochKey(epoch);
        var setHash = EvidenceSetRules.SetHash(versions);
        if (await store.CurrentAsync(key, cancellationToken) is { Frozen: true } || !await store.ReplaceAsync(new NewEvidenceSet(key, versions, setHash, EvidenceSetRules.ModeOwner, actor, reason), cancellationToken))
        {
            return Result.Failure<EvidenceSetReplaced>(Error.Conflict(FrozenCode, "A shadow row of this epoch is already stored, so its evidence set is frozen and cannot be replaced."));
        }

        ShadowLog.EvidenceSetReplaced(logger, versions.Count);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.EvidenceSetReplaced, null, $"edge_epoch={epoch?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} set={setHash[..12]} versions={versions.Count}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write, and nothing of its failure is logged but that it happened.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            ShadowLog.AuditFailed(logger);
        }

        return Result.Success(new EvidenceSetReplaced(epoch, setHash, versions.Count));
    }
}

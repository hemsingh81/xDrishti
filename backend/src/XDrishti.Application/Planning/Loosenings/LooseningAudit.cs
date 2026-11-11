using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Planning;

/// <summary>One account event per dismissal or setting change (BK-10b), written after the stored row; a failure of this second write is logged by type and does not undo the row (the precedent of the other owner acts). Never a password.</summary>
internal static class LooseningAudit
{
    public static async ValueTask RecordAsync(IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger logger, AccountEventType type, string text, string actor)
    {
        try
        {
            events.Record(AccountEvent.Record(type, null, text, actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write. Only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            LooseningLog.AuditFailed(logger, failure.GetType().Name);
        }
    }
}

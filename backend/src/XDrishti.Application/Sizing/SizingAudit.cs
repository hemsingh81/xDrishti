using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>One account event <c>SizingChanged</c> per accepted change (BK-10): key, old and new value, direction, version and hash. Never a password. A failure of this second write is logged by type and does not undo the stored row (the precedent of the other owner acts).</summary>
internal static class SizingAudit
{
    public static async ValueTask RecordAsync(IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger logger, IEnumerable<SizingChange> changes, long version)
    {
        try
        {
            foreach (var c in changes)
            {
                events.Record(AccountEvent.Record(AccountEventType.SizingChanged, null, string.Create(CultureInfo.InvariantCulture, $"key={c.Key} old={c.OldValue} new={c.NewValue} direction={c.Direction} version={version} hash={c.Hash}"), c.Actor, clock.GetUtcNow()));
            }

            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write. Only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            SizingLog.AuditFailed(logger, failure.GetType().Name);
        }
    }
}

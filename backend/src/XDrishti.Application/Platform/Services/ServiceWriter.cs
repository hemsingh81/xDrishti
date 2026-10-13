using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Stores new settings for a service and writes the audit event with them, in one save: creates the row on the first save, refuses a save made on an older revision.</summary>
internal sealed class ServiceWriter(IServiceScheduleStore store, IAccountEventLog events, IPlatformUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>Stores the (checked) settings as they are.</summary>
    /// <param name="service">The service with the settings now in force.</param>
    /// <param name="next">The settings to store.</param>
    /// <param name="expectedRevision">The revision the caller read: a row that has another one is refused (<c>services.revision_changed</c>); 0 means the service had no row.</param>
    /// <param name="type">The audit event.</param>
    /// <param name="detail">What changed, in words.</param>
    /// <param name="actor">Who did it.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public ValueTask<Result> WriteAsync(EffectiveService service, ServiceSettings next, long expectedRevision, AccountEventType type, string detail, string actor, CancellationToken cancellationToken) =>
        ApplyAsync(service, _ => next, expectedRevision, type, _ => detail, actor, cancellationToken);

    /// <summary>
    /// Changes the settings that are stored <i>now</i> (read again with the row, guarded by its version): a pause or a resume flips the switch and keeps whatever else was saved meanwhile instead of writing back
    /// what the caller read earlier. A row whose schedule cannot be read is changed from the service's effective settings (the defaults).
    /// </summary>
    public ValueTask<Result> ChangeAsync(EffectiveService service, Func<ServiceSettings, ServiceSettings> change, AccountEventType type, Func<ServiceSettings, ServiceSettings, string> describe, string actor, CancellationToken cancellationToken) =>
        ApplyAsync(service, change, null, type, null, actor, cancellationToken, describe);

    private async ValueTask<Result> ApplyAsync(
        EffectiveService service,
        Func<ServiceSettings, ServiceSettings> change,
        long? expectedRevision,
        AccountEventType type,
        Func<ServiceSettings, string>? detail,
        string actor,
        CancellationToken cancellationToken,
        Func<ServiceSettings, ServiceSettings, string>? describe = null)
    {
        var now = clock.GetUtcNow();
        var row = await store.FindAsync(service.Id, cancellationToken);
        var current = row is { ScheduleReadable: true } ? row.Settings : service.Settings;
        var next = change(current);
        if (row is null)
        {
            if (expectedRevision is not null and not 0)
            {
                return Result.Failure(ServiceErrors.RevisionChanged);
            }

            store.Add(ServiceSchedule.Create(service.Id, next, actor, now));
        }
        else
        {
            if (expectedRevision is { } revision && revision != row.Revision)
            {
                return Result.Failure(ServiceErrors.RevisionChanged);
            }

            row.Apply(next, actor, now);
        }

        events.Record(AccountEvent.Record(type, null, describe is null ? detail!(next) : describe(current, next), actor, now));
        return await unitOfWork.TrySaveAsync(cancellationToken) ? Result.Success() : Result.Failure(ServiceErrors.RevisionChanged);
    }
}

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// Saves the choices for a service (ADR 0018 §1, §6) and audits the change: the schedule, retries, timeout, catch-up, notify on failure, parameters and whether it is on. Checked against the whole set of
/// services first; <c>services.invalid</c> (each error named with its field and service, nothing stored) when anything is wrong, warnings are returned with the service and do not block. The
/// <see cref="ServiceScheduleInput.Revision"/> must be the one read: <c>services.revision_changed</c> otherwise (also when two saves race). <c>services.not_found</c> for an unknown id,
/// <c>services.read_only</c> for the account sync. A save that changes nothing stores and audits nothing. Takes effect at the scheduler's next look, no restart.
/// </summary>
public sealed record SaveServiceScheduleCommand(string ServiceId, ServiceScheduleInput Input, string Actor) : ICommand;

internal sealed class SaveServiceScheduleHandler(ServiceRegistry registry, ServiceSettingsReader settings, ServiceWriter writer, ServiceViewReader reader)
    : ICommandHandler<SaveServiceScheduleCommand, ServiceView>
{
    public async ValueTask<Result<ServiceView>> HandleAsync(SaveServiceScheduleCommand command, CancellationToken cancellationToken)
    {
        if (command.ServiceId == ServiceIds.AccountSync)
        {
            return Result.Failure<ServiceView>(ServiceErrors.ReadOnly);
        }

        if (registry.Find(command.ServiceId) is null)
        {
            return Result.Failure<ServiceView>(ServiceErrors.NotFound);
        }

        var all = await settings.ReadAllAsync(cancellationToken);
        var service = all.First(s => s.Id == command.ServiceId);
        var issues = ServiceInputChecks.Evaluate(all, service, command.Input, out var parsed);
        if (issues.Any(i => i.IsError) || parsed is null)
        {
            return Result.Failure<ServiceView>(ServiceErrors.Invalid(issues, service.Id));
        }

        if (service.Row is not null && !service.StoredInvalid && service.Row.Revision == command.Input.Revision && parsed.Same(service.Settings))
        {
            return Result.Success((await reader.ReadAsync(service.Id, cancellationToken))!);
        }

        var detail = ServiceChangeSummary.Describe(service.Id, service.Settings, parsed, service.NotBeforeSessionEnd, registry.NameOf);
        var written = await writer.WriteAsync(service, parsed, command.Input.Revision, AccountEventType.ServiceScheduleChanged, detail, command.Actor, cancellationToken);
        return written.IsFailure
            ? Result.Failure<ServiceView>(written.Error)
            : Result.Success((await reader.ReadAsync(service.Id, cancellationToken))!);
    }
}

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// "Restore default" (ADR 0018 §1): writes the definition's default choices as the service's row; nothing is deleted. Audited. <c>services.invalid</c> if the defaults would break a rule against the other
/// services (nothing is stored), <c>services.not_found</c>, <c>services.read_only</c>.
/// </summary>
public sealed record ResetServiceScheduleCommand(string ServiceId, string Actor) : ICommand;

internal sealed class ResetServiceScheduleHandler(ServiceRegistry registry, ServiceSettingsReader settings, ServiceWriter writer, ServiceViewReader reader)
    : ICommandHandler<ResetServiceScheduleCommand, ServiceView>
{
    public async ValueTask<Result<ServiceView>> HandleAsync(ResetServiceScheduleCommand command, CancellationToken cancellationToken)
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
        var defaults = service.Definition.Defaults with { Parameters = service.Definition.WithDefaults(service.Definition.Defaults.Parameters) };
        var errors = ScheduleRules.Evaluate(ServiceMapper.Subjects(all, service.Id, defaults)).Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            return Result.Failure<ServiceView>(ServiceErrors.Invalid(errors, service.Id));
        }

        var detail = ServiceChangeSummary.Describe(service.Id, service.Settings, defaults, service.NotBeforeSessionEnd, registry.NameOf);
        var written = await writer.WriteAsync(service, defaults, service.Revision, AccountEventType.ServiceScheduleChanged, "Restored the default. " + detail, command.Actor, cancellationToken);
        return written.IsFailure
            ? Result.Failure<ServiceView>(written.Error)
            : Result.Success((await reader.ReadAsync(service.Id, cancellationToken))!);
    }
}

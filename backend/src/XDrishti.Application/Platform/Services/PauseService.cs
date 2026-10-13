using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// Pauses a service (ADR 0018 §3, §4): it is no longer started by its schedule, by a catch-up or after another service, and its past times are not made up for when it is resumed. A run in progress is
/// not stopped; Run now still works. Audited. A service that is paused already is returned as it is. <c>services.not_found</c>, <c>services.read_only</c>.
/// </summary>
public sealed record PauseServiceCommand(string ServiceId, string Actor) : ICommand;

internal sealed class PauseServiceHandler(ServiceRegistry registry, ServiceSettingsReader settings, ServiceWriter writer, ServiceViewReader reader)
    : ICommandHandler<PauseServiceCommand, ServiceView>
{
    public async ValueTask<Result<ServiceView>> HandleAsync(PauseServiceCommand command, CancellationToken cancellationToken) =>
        await ServiceToggle.SetEnabledAsync(command.ServiceId, enabled: false, command.Actor, registry, settings, writer, reader, cancellationToken);
}

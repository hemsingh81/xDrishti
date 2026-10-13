using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>Resumes a paused service: it follows its schedule again from now on (a time that passed while it was paused is not made up for). Audited. A service that is on already is returned as it is. <c>services.not_found</c>, <c>services.read_only</c>.</summary>
public sealed record ResumeServiceCommand(string ServiceId, string Actor) : ICommand;

internal sealed class ResumeServiceHandler(ServiceRegistry registry, ServiceSettingsReader settings, ServiceWriter writer, ServiceViewReader reader)
    : ICommandHandler<ResumeServiceCommand, ServiceView>
{
    public async ValueTask<Result<ServiceView>> HandleAsync(ResumeServiceCommand command, CancellationToken cancellationToken) =>
        await ServiceToggle.SetEnabledAsync(command.ServiceId, enabled: true, command.Actor, registry, settings, writer, reader, cancellationToken);
}

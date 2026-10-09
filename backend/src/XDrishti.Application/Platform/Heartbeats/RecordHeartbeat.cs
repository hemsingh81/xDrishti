using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Platform.Heartbeats;

/// <summary>A background service reports that it is alive.</summary>
public sealed record RecordHeartbeatCommand(string ServiceName, string Instance, string Version) : ICommand;

internal sealed class RecordHeartbeatHandler(IServiceHeartbeatStore store, TimeProvider clock)
    : ICommandHandler<RecordHeartbeatCommand>
{
    public async ValueTask<Result> HandleAsync(RecordHeartbeatCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ServiceName))
        {
            return Result.Failure(Error.Validation("heartbeat.service_required", "Service name is required."));
        }

        var now = clock.GetUtcNow();
        var heartbeat = await store.FindAsync(command.ServiceName, cancellationToken);
        if (heartbeat is null)
        {
            store.Add(ServiceHeartbeat.Start(command.ServiceName, command.Instance, command.Version, now));
        }
        else
        {
            heartbeat.Beat(command.Instance, command.Version, now);
        }

        await store.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

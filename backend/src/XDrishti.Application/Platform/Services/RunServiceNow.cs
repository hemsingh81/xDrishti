using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// "Run now" (ADR 0018 §7): audits the request and starts the service through the same path as its schedule, trigger <c>manual</c>, whether it is paused or not. Returns the run that started (running; the work
/// goes on in the background). <c>services.already_running</c> when it runs (or waits for a retry) already — a skipped run is recorded; <c>services.not_found</c>, <c>services.read_only</c>.
/// </summary>
public sealed record RunServiceNowCommand(string ServiceId, string Actor) : ICommand;

internal sealed class RunServiceNowHandler(ServiceRegistry registry, ServiceRunner runner, IAccountEventLog events, IPlatformUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RunServiceNowCommand, ServiceRunView>
{
    public async ValueTask<Result<ServiceRunView>> HandleAsync(RunServiceNowCommand command, CancellationToken cancellationToken)
    {
        if (command.ServiceId == ServiceIds.AccountSync)
        {
            return Result.Failure<ServiceRunView>(ServiceErrors.ReadOnly);
        }

        if (registry.Find(command.ServiceId) is null)
        {
            return Result.Failure<ServiceRunView>(ServiceErrors.NotFound);
        }

        events.Record(AccountEvent.Record(AccountEventType.ServiceRunRequested, null, $"{command.ServiceId}: run now", command.Actor, clock.GetUtcNow()));
        await unitOfWork.TrySaveAsync(cancellationToken);

        var start = await runner.StartAsync(command.ServiceId, ServiceRunTrigger.Manual, command.Actor, cancellationToken);
        return start is { AlreadyRunning: false, Run: { } run }
            ? Result.Success(ServiceRunView.From(run))
            : Result.Failure<ServiceRunView>(start.Unknown ? ServiceErrors.NotFound : ServiceErrors.AlreadyRunning);
    }
}

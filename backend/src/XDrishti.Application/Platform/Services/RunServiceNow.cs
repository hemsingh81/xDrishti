using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// "Run now" (ADR 0018 §7): audits the request and starts the service through the same path as its schedule, trigger <c>manual</c>, whether it is paused or not. Returns the run that started (running; the work
/// goes on in the background). <c>services.already_running</c> when it runs (or waits for a retry) already — a skipped run is recorded; <c>services.not_found</c>, <c>services.read_only</c>. A service that another process runs
/// (the live feed, <see cref="IExternallyRunService"/>) is not started here: the request is audited and the manual-run marker is written (<see cref="IFeedManualRun"/>, <see cref="FeedManualRunSettings.Duration"/> from now), which
/// the process reads within 15 seconds and then opens the run itself. The answer is then a <b>queued</b> run (status <c>queued</c>, an empty id: the record does not exist yet) - honest about what happened, and not "running", which
/// the Services screen would take as a start to follow.
/// </summary>
public sealed record RunServiceNowCommand(string ServiceId, string Actor) : ICommand;

internal sealed class RunServiceNowHandler(
    ServiceRegistry registry,
    ServiceRunner runner,
    IServiceRunStore runs,
    IFeedManualRun manualRun,
    FeedManualRunSettings manualSettings,
    IAccountEventLog events,
    IPlatformUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<RunServiceNowCommand, ServiceRunView>
{
    /// <summary>The status of a run that was asked for in another process and does not exist yet.</summary>
    public const string QueuedStatus = "queued";

    public const string QueuedMessage = "Requested. The live feed opens its run within 15 seconds.";

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

        if (command.ServiceId == XDrishti.Application.Depth.DepthRecorderService.Id)
        {
            return Result.Failure<ServiceRunView>(ServiceErrors.RunNowUnavailable); // the depth recorder follows its three gates and its window; "Run now" cannot start it (P7-16)
        }

        if (registry.IsExternal(command.ServiceId))
        {
            return await RequestExternalAsync(command, cancellationToken);
        }

        events.Record(AccountEvent.Record(AccountEventType.ServiceRunRequested, null, $"{command.ServiceId}: run now", command.Actor, clock.GetUtcNow()));
        await unitOfWork.TrySaveAsync(cancellationToken);

        var start = await runner.StartAsync(command.ServiceId, ServiceRunTrigger.Manual, command.Actor, cancellationToken);
        return start is { AlreadyRunning: false, Run: { } run }
            ? Result.Success(ServiceRunView.From(run))
            : Result.Failure<ServiceRunView>(start.Unknown || start.RunsElsewhere ? ServiceErrors.NotFound : ServiceErrors.AlreadyRunning);
    }

    /// <summary>The service runs in another process: write down the request and the marker (one save), and say it is queued.</summary>
    private async ValueTask<Result<ServiceRunView>> RequestExternalAsync(RunServiceNowCommand command, CancellationToken cancellationToken)
    {
        if ((await runs.GetRunningServicesAsync(cancellationToken)).Contains(command.ServiceId))
        {
            return Result.Failure<ServiceRunView>(ServiceErrors.AlreadyRunning);
        }

        var now = clock.GetUtcNow();
        events.Record(AccountEvent.Record(AccountEventType.ServiceRunRequested, null, $"{command.ServiceId}: run now", command.Actor, now));
        await manualRun.SetManualUntilAsync(now + manualSettings.Duration, command.Actor, now, cancellationToken);
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<ServiceRunView>(ServiceErrors.AlreadyRunning); // a second request wrote the marker first
        }

        return Result.Success(new ServiceRunView(Guid.Empty, command.ServiceId, ServiceRunTrigger.Manual.ToCode(), 1, now, null, QueuedStatus, QueuedMessage, null));
    }
}

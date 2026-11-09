using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>The owner asks for a recovery pass now (spec AU-04): audited, not debounced, and refused with <c>recovery.already_running</c> while a pass runs. The pass is the normal one (same plan, same limits); nothing here starts an order.</summary>
public sealed record RetryRecoveryCommand(string Actor) : ICommand;

/// <summary><c>Started</c> is true when the pass has steps to run (<c>Steps</c>); false when there is nothing to run, so the pass only proves partial items again.</summary>
public sealed record RecoveryRetryAccepted(DateTimeOffset RequestedAt, bool Started, int Steps);

public static class RecoveryErrors
{
    public const string AlreadyRunningCode = "recovery.already_running";

    public const string NotRecordedCode = "recovery.not_recorded";

    public static readonly Error AlreadyRunning = Error.Conflict(AlreadyRunningCode, "A recovery pass is running already.");

    public static readonly Error NotRecorded = Error.Conflict(NotRecordedCode, "The request could not be recorded. Try again.");
}

internal sealed class RetryRecoveryHandler(RecoveryRunner runner, IAccountEventLog events, IPlatformUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RetryRecoveryCommand, RecoveryRetryAccepted>
{
    public async ValueTask<Result<RecoveryRetryAccepted>> HandleAsync(RetryRecoveryCommand command, CancellationToken cancellationToken)
    {
        if (runner.IsRunning)
        {
            return Result.Failure<RecoveryRetryAccepted>(RecoveryErrors.AlreadyRunning);
        }

        var steps = (await runner.PreviewAsync(true, cancellationToken)) is { IsSuccess: true } plan ? plan.Value.Steps.Count : 0;
        if (!runner.Request(RecoveryTrigger.Manual))
        {
            return Result.Failure<RecoveryRetryAccepted>(RecoveryErrors.AlreadyRunning);
        }

        var now = clock.GetUtcNow();
        events.Record(AccountEvent.Record(AccountEventType.ServiceRunRequested, null, "recovery: run now", command.Actor, now));
        return await unitOfWork.TrySaveAsync(cancellationToken)
            ? Result.Success(new RecoveryRetryAccepted(now, steps > 0, steps))
            : Result.Failure<RecoveryRetryAccepted>(RecoveryErrors.NotRecorded); // the pass runs all the same; only the audit row is missing
    }
}

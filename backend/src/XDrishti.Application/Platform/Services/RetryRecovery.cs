using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>The owner asks for a recovery pass now (spec AU-04): audited, not debounced, and refused with <c>recovery.already_running</c> while a pass runs. The pass is the normal one (same plan, same limits); nothing here starts an order.</summary>
public sealed record RetryRecoveryCommand(string Actor) : ICommand;

public sealed record RecoveryRetryAccepted(DateTimeOffset RequestedAt);

public static class RecoveryErrors
{
    public const string AlreadyRunningCode = "recovery.already_running";

    public static readonly Error AlreadyRunning = Error.Conflict(AlreadyRunningCode, "A recovery pass is running already.");
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

        var now = clock.GetUtcNow();
        events.Record(AccountEvent.Record(AccountEventType.ServiceRunRequested, null, "recovery: run now", command.Actor, now));
        await unitOfWork.TrySaveAsync(cancellationToken);
        return runner.Request(RecoveryTrigger.Manual)
            ? Result.Success(new RecoveryRetryAccepted(now))
            : Result.Failure<RecoveryRetryAccepted>(RecoveryErrors.AlreadyRunning);
    }
}

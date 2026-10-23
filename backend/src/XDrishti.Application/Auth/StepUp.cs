using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Re-enter the password to allow sensitive actions for <see cref="AuthSession.ElevationDuration"/>.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="Password">The password re-entered.</param>
/// <param name="Client">Where from.</param>
/// <param name="Elevate">False only for a one-off confirmation of the password for a single act (P7-12 autonomy settings): the same lockout counters and audit, but the session's elevation window is neither opened nor extended.</param>
public sealed record StepUpCommand(Guid SessionId, string? Password, ClientInfo Client, bool Elevate = true) : ICommand;

/// <param name="ElevatedUntil">End of the window in which sensitive actions are allowed.</param>
public sealed record StepUpResult(DateTimeOffset ElevatedUntil);

/// <summary>
/// Same lockout counters and rules as <see cref="LoginHandler"/>: a wrong password counts as a failure, the fifth one locks the
/// account, and a locked account is refused without looking at the password. A correct password resets the counter.
/// </summary>
internal sealed class StepUpHandler(
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    TimeProvider clock,
    ILogger<StepUpHandler> logger) : ICommandHandler<StepUpCommand, StepUpResult>
{
    public async ValueTask<Result<StepUpResult>> HandleAsync(StepUpCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
        if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
        {
            return Result.Failure<StepUpResult>(AuthErrors.Unauthenticated);
        }

        var password = command.Password ?? string.Empty;
        if (found.Owner.IsLocked(now))
        {
            return await RefuseLockedAsync(found.Owner, now, command.Client, cancellationToken);
        }

        // The slow password check happens once; only the cheap state change is repeated when a concurrent request got in between.
        var correct = password.Length is > 0 and <= AuthLimits.MaxPasswordLength
            && hasher.Verify(found.Owner.PasswordHash, password) != PasswordVerificationResult.Failed;

        for (var attempt = 1; attempt <= LoginHandler.MaxConcurrencyAttempts; attempt++)
        {
            if (attempt > 1)
            {
                now = clock.GetUtcNow();
                found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
                if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
                {
                    return Result.Failure<StepUpResult>(AuthErrors.Unauthenticated);
                }

                if (found.Owner.IsLocked(now))
                {
                    return await RefuseLockedAsync(found.Owner, now, command.Client, cancellationToken);
                }
            }

            var outcome = correct
                ? await TryAcceptAsync(found, now, command.Client, command.Elevate, cancellationToken)
                : await TryRejectAsync(found.Owner, now, command.Client, cancellationToken);
            if (outcome is not null)
            {
                return outcome;
            }
        }

        AuthLog.ConcurrencyGaveUp(logger, LoginHandler.MaxConcurrencyAttempts);
        return Result.Failure<StepUpResult>(AuthErrors.Busy);
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result<StepUpResult>?> TryAcceptAsync(SessionWithOwner found, DateTimeOffset now, ClientInfo client, bool elevate, CancellationToken cancellationToken)
    {
        found.Owner.RegisterSuccess();
        var until = elevate ? found.Session.Elevate(now) : found.Session.ElevatedUntil ?? now;
        audit.Record(AuditEvent.Record(AuditEventType.StepUpSucceeded, now, found.Owner.Email, client.Ip, client.UserAgent));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        var logEmail = LogSanitizer.Email(found.Owner.Email);
        AuthLog.StepUpSucceeded(logger, logEmail, (int)AuthSession.ElevationDuration.TotalMinutes);
        return Result.Success(new StepUpResult(until));
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result<StepUpResult>?> TryRejectAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        var lockedNow = owner.RegisterFailure(now);
        audit.Record(AuditEvent.Record(AuditEventType.StepUpFailed, now, owner.Email, client.Ip, client.UserAgent));
        if (lockedNow)
        {
            audit.Record(AuditEvent.Record(AuditEventType.AccountLocked, now, owner.Email, client.Ip, client.UserAgent));
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        AuthLog.StepUpFailed(logger, LogSanitizer.Email(owner.Email));
        if (lockedNow)
        {
            AuthLog.AccountLocked(logger, LogSanitizer.Email(owner.Email), (int)OwnerAccount.LockoutDuration.TotalMinutes);
        }

        return Result.Failure<StepUpResult>(lockedNow ? AuthErrors.Locked(owner.RemainingLock(now)) : AuthErrors.InvalidCredentials);
    }

    private async ValueTask<Result<StepUpResult>> RefuseLockedAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        audit.Record(AuditEvent.Record(AuditEventType.StepUpFailed, now, owner.Email, client.Ip, client.UserAgent, "account locked"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        AuthLog.StepUpRefusedLocked(logger, LogSanitizer.Email(owner.Email));
        return Result.Failure<StepUpResult>(AuthErrors.Locked(owner.RemainingLock(now)));
    }
}

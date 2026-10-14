using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>The signed-in owner chooses a new password. The current one must be re-entered; every other session is signed out.</summary>
public sealed record ChangePasswordCommand(Guid SessionId, string? CurrentPassword, string? NewPassword, ClientInfo Client) : ICommand;

/// <summary>
/// Same lockout counters and rules as <see cref="StepUpHandler"/> and <see cref="LoginHandler"/>: a wrong current password counts as a failure,
/// the fifth one locks the account, and a locked account is refused without looking at the password. A bad new password is refused before
/// anything is verified (it says nothing about the current one) and does not count as a failure.
/// </summary>
internal sealed class ChangePasswordHandler(
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    TimeProvider clock,
    ILogger<ChangePasswordHandler> logger) : ICommandHandler<ChangePasswordCommand>
{
    public async ValueTask<Result> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
        if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
        {
            return Result.Failure(AuthErrors.Unauthenticated);
        }

        if (found.Owner.IsLocked(now))
        {
            return await RefuseLockedAsync(found.Owner, now, command.Client, cancellationToken);
        }

        if (!OwnerCredentials.IsAcceptableNewPassword(command.NewPassword))
        {
            return Result.Failure(AuthErrors.PasswordInvalid());
        }

        var newPassword = command.NewPassword!;
        var current = command.CurrentPassword ?? string.Empty;

        // The slow hashing happens once each; only the cheap state change is repeated when a concurrent request got in between.
        var correct = current.Length is > 0 and <= AuthLimits.MaxPasswordLength
            && hasher.Verify(found.Owner.PasswordHash, current) != PasswordVerificationResult.Failed;
        if (correct && string.Equals(current, newPassword, StringComparison.Ordinal))
        {
            return Result.Failure(AuthErrors.PasswordUnchanged());
        }

        var newHash = correct ? hasher.Hash(newPassword) : null;

        for (var attempt = 1; attempt <= LoginHandler.MaxConcurrencyAttempts; attempt++)
        {
            if (attempt > 1)
            {
                now = clock.GetUtcNow();
                found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
                if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
                {
                    return Result.Failure(AuthErrors.Unauthenticated);
                }

                if (found.Owner.IsLocked(now))
                {
                    return await RefuseLockedAsync(found.Owner, now, command.Client, cancellationToken);
                }
            }

            var outcome = newHash is not null
                ? await TryAcceptAsync(found, newHash, now, command.Client, cancellationToken)
                : await TryRejectAsync(found.Owner, now, command.Client, cancellationToken);
            if (outcome is not null)
            {
                return outcome;
            }
        }

        AuthLog.ConcurrencyGaveUp(logger, LoginHandler.MaxConcurrencyAttempts);
        return Result.Failure(AuthErrors.Busy);
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result?> TryAcceptAsync(SessionWithOwner found, string newHash, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        var owner = found.Owner;
        owner.ChangePassword(newHash, now);

        var others = (await sessions.ListNotRevokedAsync(owner.Id, cancellationToken)).Where(s => s.Id != found.Session.Id).ToList();
        foreach (var session in others)
        {
            session.Revoke(now);
        }

        audit.Record(AuditEvent.Record(AuditEventType.PasswordChanged, now, owner.Email, client.Ip, client.UserAgent, $"{others.Count} other session(s) signed out"));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        var logEmail = LogSanitizer.Email(owner.Email);
        AuthLog.PasswordChanged(logger, logEmail, others.Count);
        return Result.Success();
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result?> TryRejectAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        var lockedNow = owner.RegisterFailure(now);
        audit.Record(AuditEvent.Record(AuditEventType.StepUpFailed, now, owner.Email, client.Ip, client.UserAgent, "password change: wrong current password"));
        if (lockedNow)
        {
            audit.Record(AuditEvent.Record(AuditEventType.AccountLocked, now, owner.Email, client.Ip, client.UserAgent));
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        AuthLog.PasswordChangeFailed(logger, LogSanitizer.Email(owner.Email));
        if (lockedNow)
        {
            AuthLog.AccountLocked(logger, LogSanitizer.Email(owner.Email), (int)OwnerAccount.LockoutDuration.TotalMinutes);
        }

        return Result.Failure(lockedNow ? AuthErrors.Locked(owner.RemainingLock(now)) : AuthErrors.InvalidCredentials);
    }

    private async ValueTask<Result> RefuseLockedAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        audit.Record(AuditEvent.Record(AuditEventType.StepUpFailed, now, owner.Email, client.Ip, client.UserAgent, "password change: account locked"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        AuthLog.PasswordChangeRefusedLocked(logger, LogSanitizer.Email(owner.Email));
        return Result.Failure(AuthErrors.Locked(owner.RemainingLock(now)));
    }
}

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Sign in with e-mail and password.</summary>
public sealed record LoginCommand(string? Email, string? Password, ClientInfo Client) : ICommand;

/// <param name="Session">What the client may see.</param>
/// <param name="SessionToken">The opaque token for the session cookie. Shown once, never stored in clear.</param>
public sealed record LoginResult(SessionInfo Session, string SessionToken);

internal sealed class LoginHandler(
    IOwnerAccountStore owners,
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    ITokenGenerator tokens,
    TimeProvider clock,
    ILogger<LoginHandler> logger) : ICommandHandler<LoginCommand, LoginResult>
{
    /// <summary>
    /// Bounds the optimistic-concurrency retries. Every round at least one contender saves, and at most
    /// <see cref="OwnerAccount.MaxFailedAttempts"/> failures can be saved before the account is locked and everyone else
    /// is refused without writing, so this is far more than concurrent requests can ever need.
    /// </summary>
    internal const int MaxConcurrencyAttempts = 10;

    public async ValueTask<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var email = OwnerAccount.NormalizeEmail(command.Email);
        var password = command.Password ?? string.Empty;
        var withinLimits = email.Length is > 0 and <= AuthLimits.MaxEmailLength && password.Length is > 0 and <= AuthLimits.MaxPasswordLength;

        var owner = withinLimits ? await owners.FindByEmailAsync(email, cancellationToken) : null;
        if (owner is null)
        {
            // Same work and same answer as for a wrong password: the response must not reveal which e-mails exist.
            hasher.SimulateVerification(password);
            await RecordAsync(AuditEventType.LoginFailed, now, email, command.Client, cancellationToken);
            AuthLog.LoginFailed(logger, LogSanitizer.Email(email));
            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        if (owner.IsLocked(now))
        {
            // Refused without looking at the password: a correct one must not unlock early.
            return await RefuseLockedAsync(owner, now, command.Client, cancellationToken);
        }

        // The slow password check happens once. Only the cheap state change below is repeated when a concurrent request got in between.
        var verification = hasher.Verify(owner.PasswordHash, password);
        var upgradedHash = verification == PasswordVerificationResult.SuccessRehashNeeded ? hasher.Hash(password) : null;

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            if (attempt > 1)
            {
                // Another request changed the owner (failure counter, lock) since we read it: start from what is stored now.
                now = clock.GetUtcNow();
                owner = await owners.FindByEmailAsync(email, cancellationToken);
                if (owner is null)
                {
                    return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
                }

                if (owner.IsLocked(now))
                {
                    return await RefuseLockedAsync(owner, now, command.Client, cancellationToken);
                }
            }

            var outcome = verification == PasswordVerificationResult.Failed
                ? await TryRejectAsync(owner, now, command.Client, cancellationToken)
                : await TryAcceptAsync(owner, upgradedHash, now, command.Client, cancellationToken);
            if (outcome is not null)
            {
                return outcome;
            }
        }

        AuthLog.ConcurrencyGaveUp(logger, MaxConcurrencyAttempts);
        return Result.Failure<LoginResult>(AuthErrors.Busy);
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result<LoginResult>?> TryAcceptAsync(OwnerAccount owner, string? upgradedHash, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        owner.RegisterSuccess();
        if (upgradedHash is not null)
        {
            owner.UpgradePasswordHash(upgradedHash);
        }

        var token = tokens.NewSessionToken();
        var csrf = tokens.DeriveCsrfToken(token);
        var session = AuthSession.Start(owner.Id, tokens.Hash(token), now, client.Ip, client.UserAgent);
        sessions.Add(session);
        audit.Record(AuditEvent.Record(AuditEventType.LoginSucceeded, now, owner.Email, client.Ip, client.UserAgent));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        if (upgradedHash is not null)
        {
            AuthLog.PasswordHashUpgraded(logger, owner.Email);
        }

        AuthLog.LoginSucceeded(logger, owner.Email);
        var info = new SessionInfo(new UserInfo(owner.Email, owner.DisplayName), csrf, (int)AuthSession.IdleTimeout.TotalSeconds, session.ExpiresAt);
        return Result.Success(new LoginResult(info, token));
    }

    /// <summary>Returns null when the save lost against a concurrent change (nothing was saved).</summary>
    private async ValueTask<Result<LoginResult>?> TryRejectAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        var lockedNow = owner.RegisterFailure(now);
        audit.Record(AuditEvent.Record(AuditEventType.LoginFailed, now, owner.Email, client.Ip, client.UserAgent));
        if (lockedNow)
        {
            audit.Record(AuditEvent.Record(AuditEventType.AccountLocked, now, owner.Email, client.Ip, client.UserAgent));
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return null;
        }

        AuthLog.LoginFailed(logger, owner.Email);
        if (lockedNow)
        {
            AuthLog.AccountLocked(logger, owner.Email, (int)OwnerAccount.LockoutDuration.TotalMinutes);
        }

        return Result.Failure<LoginResult>(lockedNow ? AuthErrors.Locked(owner.RemainingLock(now)) : AuthErrors.InvalidCredentials);
    }

    private async ValueTask<Result<LoginResult>> RefuseLockedAsync(OwnerAccount owner, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        await RecordAsync(AuditEventType.LoginRefusedLocked, now, owner.Email, client, cancellationToken);
        AuthLog.LoginRefusedLocked(logger, owner.Email);
        return Result.Failure<LoginResult>(AuthErrors.Locked(owner.RemainingLock(now)));
    }

    private async ValueTask RecordAsync(AuditEventType type, DateTimeOffset now, string email, ClientInfo client, CancellationToken cancellationToken)
    {
        audit.Record(AuditEvent.Record(type, now, email, client.Ip, client.UserAgent));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

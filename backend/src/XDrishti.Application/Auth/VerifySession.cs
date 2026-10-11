using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Checks the session token from the cookie on a request; slides the idle window and ends expired sessions.</summary>
public sealed record VerifySessionQuery(string SessionToken, ClientInfo Client) : IQuery<Result<VerifiedSession>>;

/// <param name="SessionId">Identifies the session (for logout).</param>
/// <param name="OwnerId">The signed-in owner.</param>
/// <param name="Info">The session facts shown to the client.</param>
public sealed record VerifiedSession(Guid SessionId, Guid OwnerId, SessionInfo Info)
{
    /// <summary>True when the password was re-entered recently (the step-up window is open) at the time the session was verified.</summary>
    public bool IsElevated => Info.ElevatedUntil is not null;
}

internal sealed class VerifySessionHandler(
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    ITokenGenerator tokens,
    TimeProvider clock,
    ILogger<VerifySessionHandler> logger) : IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>
{
    public async ValueTask<Result<VerifiedSession>> HandleAsync(VerifySessionQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.SessionToken))
        {
            return Result.Failure<VerifiedSession>(AuthErrors.Unauthenticated);
        }

        var found = await sessions.FindByTokenHashAsync(tokens.Hash(query.SessionToken), cancellationToken);
        if (found is null)
        {
            return Result.Failure<VerifiedSession>(AuthErrors.Unauthenticated);
        }

        var (session, owner) = (found.Session, found.Owner);
        var now = clock.GetUtcNow();
        switch (session.Evaluate(now))
        {
            case SessionStatus.Revoked:
                return Result.Failure<VerifiedSession>(AuthErrors.Unauthenticated);
            case SessionStatus.IdleExpired:
                return await EndAsync(session, owner, AuditEventType.SessionExpiredIdle, "idle timeout", now, query.Client, cancellationToken);
            case SessionStatus.AbsoluteExpired:
                return await EndAsync(session, owner, AuditEventType.SessionExpiredAbsolute, "absolute lifetime", now, query.Client, cancellationToken);
        }

        if (session.TryTouch(now))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var info = new SessionInfo(
            new UserInfo(owner.Email, owner.DisplayName),
            tokens.DeriveCsrfToken(query.SessionToken),
            (int)AuthSession.IdleTimeout.TotalSeconds,
            session.ExpiresAt,
            session.IsElevated(now) ? session.ElevatedUntil : null);
        return Result.Success(new VerifiedSession(session.Id, owner.Id, info));
    }

    private async ValueTask<Result<VerifiedSession>> EndAsync(
        AuthSession session, OwnerAccount owner, AuditEventType type, string reason, DateTimeOffset now, ClientInfo client, CancellationToken cancellationToken)
    {
        session.Revoke(now);
        audit.Record(AuditEvent.Record(type, now, owner.Email, client.Ip, client.UserAgent));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        AuthLog.SessionExpired(logger, owner.Email, reason);
        return Result.Failure<VerifiedSession>(AuthErrors.SessionExpired);
    }
}

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Ends one session. Other sessions of the owner stay valid.</summary>
public sealed record LogoutCommand(Guid SessionId, ClientInfo Client) : ICommand;

internal sealed class LogoutHandler(
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<LogoutHandler> logger) : ICommandHandler<LogoutCommand>
{
    public async ValueTask<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
        if (found is null || found.Session.RevokedAt is not null)
        {
            return Result.Success(); // already gone: logging out twice is fine
        }

        var now = clock.GetUtcNow();
        found.Session.Revoke(now);
        audit.Record(AuditEvent.Record(AuditEventType.Logout, now, found.Owner.Email, command.Client.Ip, command.Client.UserAgent));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var logEmail = LogSanitizer.Email(found.Owner.Email);
        AuthLog.LoggedOut(logger, logEmail);
        return Result.Success();
    }
}

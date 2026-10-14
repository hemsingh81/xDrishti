using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Replaces the owner's credentials (the way to change the password) and signs the owner out everywhere.</summary>
public sealed record ResetOwnerPasswordCommand(string? Email, string? DisplayName, string? Password) : ICommand;

internal sealed class ResetOwnerPasswordHandler(
    IOwnerAccountStore owners,
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    TimeProvider clock,
    ILogger<ResetOwnerPasswordHandler> logger) : ICommandHandler<ResetOwnerPasswordCommand>
{
    public async ValueTask<Result> HandleAsync(ResetOwnerPasswordCommand command, CancellationToken cancellationToken)
    {
        if (OwnerCredentials.Validate(command.Email, command.DisplayName, command.Password) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var owner = await owners.FindSoleOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return Result.Failure(AuthErrors.OwnerMissing);
        }

        var now = clock.GetUtcNow();
        owner.ReplaceCredentials(command.Email!, command.DisplayName!, hasher.Hash(command.Password!), now);

        var active = await sessions.ListNotRevokedAsync(owner.Id, cancellationToken);
        foreach (var session in active)
        {
            session.Revoke(now);
        }

        audit.Record(AuditEvent.Record(AuditEventType.OwnerPasswordReset, now, owner.Email, ClientInfo.Deployment.Ip, null));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AuthLog.OwnerPasswordReset(logger, LogSanitizer.Email(owner.Email), active.Count);
        return Result.Success();
    }
}

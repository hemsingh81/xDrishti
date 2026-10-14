using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Replaces the owner's display name and sign-in e-mail. The API only lets this through right after a step-up.</summary>
public sealed record UpdateProfileCommand(Guid SessionId, string? DisplayName, string? Email, ClientInfo Client) : ICommand;

internal sealed class UpdateProfileHandler(
    ISessionStore sessions,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UpdateProfileHandler> logger) : ICommandHandler<UpdateProfileCommand, AccountResponse>
{
    public async ValueTask<Result<AccountResponse>> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        if (OwnerCredentials.ValidateProfile(command.Email, command.DisplayName) is { } invalid)
        {
            return Result.Failure<AccountResponse>(invalid);
        }

        for (var attempt = 1; attempt <= LoginHandler.MaxConcurrencyAttempts; attempt++)
        {
            var now = clock.GetUtcNow();
            var found = await sessions.FindByIdAsync(command.SessionId, cancellationToken);
            if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
            {
                return Result.Failure<AccountResponse>(AuthErrors.Unauthenticated);
            }

            var owner = found.Owner;
            var previousEmail = owner.Email;
            var changed = owner.UpdateProfile(command.Email!, command.DisplayName!);
            if (changed.Count == 0)
            {
                return Result.Success(AccountResponse.From(owner));
            }

            var fields = string.Join(", ", changed);

            // Names of the changed fields only; the entry is filed under the address the owner signed in with.
            audit.Record(AuditEvent.Record(AuditEventType.ProfileUpdated, now, previousEmail, command.Client.Ip, command.Client.UserAgent, "changed: " + fields));

            // A concurrent sign-in attempt may have changed the same row (failure counter): re-read and apply again.
            if (await unitOfWork.TrySaveChangesAsync(cancellationToken))
            {
                var logEmail = LogSanitizer.Email(owner.Email);
                AuthLog.ProfileUpdated(logger, logEmail, fields);
                return Result.Success(AccountResponse.From(owner));
            }
        }

        AuthLog.ConcurrencyGaveUp(logger, LoginHandler.MaxConcurrencyAttempts);
        return Result.Failure<AccountResponse>(AuthErrors.Busy);
    }
}

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Creates the owner account at deployment when none exists. Idempotent: an existing owner is never touched.</summary>
public sealed record ProvisionOwnerCommand(string? Email, string? DisplayName, string? Password) : ICommand;

public enum ProvisionOutcome
{
    Created,
    AlreadyExists,
}

internal sealed class ProvisionOwnerHandler(
    IOwnerAccountStore owners,
    IAuditLog audit,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    TimeProvider clock,
    ILogger<ProvisionOwnerHandler> logger) : ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>
{
    public async ValueTask<Result<ProvisionOutcome>> HandleAsync(ProvisionOwnerCommand command, CancellationToken cancellationToken)
    {
        if (await owners.FindSoleOwnerAsync(cancellationToken) is not null)
        {
            AuthLog.OwnerAlreadyExists(logger);
            return Result.Success(ProvisionOutcome.AlreadyExists);
        }

        if (OwnerCredentials.Validate(command.Email, command.DisplayName, command.Password) is { } invalid)
        {
            return Result.Failure<ProvisionOutcome>(invalid);
        }

        var now = clock.GetUtcNow();
        var owner = OwnerAccount.Create(command.Email!, command.DisplayName!, hasher.Hash(command.Password!), now);
        owners.Add(owner);
        audit.Record(AuditEvent.Record(AuditEventType.OwnerProvisioned, now, owner.Email, ClientInfo.Deployment.Ip, null));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AuthLog.OwnerProvisioned(logger, owner.Email);
        return Result.Success(ProvisionOutcome.Created);
    }
}

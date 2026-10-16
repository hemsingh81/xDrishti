using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>
/// Dhan sent the owner's browser back after the consent login (<c>tokenId</c>). Needs the signed-in session that started it and the
/// session's open pending sign-in, which is used up at once. The tokenId is exchanged for a token at Dhan; the token must belong to the
/// account's client id. Always ends in a <see cref="ConnectReturn"/>: the endpoint redirects to the account with the outcome.
/// </summary>
/// <param name="SessionId">The signed-in session of the callback request; null when there is none.</param>
/// <param name="Code">Dhan's tokenId (single use; never logged).</param>
/// <param name="Actor">The signed-in owner, for the audit trail.</param>
public sealed record CompleteConnectCommand(Guid? SessionId, string? Code, string Actor) : ICommand
{
    public override string ToString() =>
        $"{nameof(CompleteConnectCommand)} {{ SessionId = {SessionId}, Code = {SecretsInput.Redact(Code)} }}";
}

internal sealed class CompleteConnectHandler(
    IAccountStore accounts,
    IPendingConnectStore pending,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    ConnectionWriter writer,
    IBrokerConnector connector,
    IAttentionReconciler attention,
    TimeProvider clock,
    ILogger<CompleteConnectHandler> logger) : ICommandHandler<CompleteConnectCommand, ConnectReturn>
{
    public async ValueTask<Result<ConnectReturn>> HandleAsync(CompleteConnectCommand command, CancellationToken cancellationToken)
    {
        if (command.SessionId is not { } sessionId)
        {
            return Refused(AuthErrors.UnauthenticatedCode, "no signed-in session");
        }

        var sign = await pending.FindLatestUnusedAsync(sessionId, cancellationToken);
        if (sign is null)
        {
            return Refused(ConnectionErrors.ConnectExpiredCode, "no matching sign-in");
        }

        var now = clock.GetUtcNow();
        var check = sign.Check(sessionId, now);
        if (check != PendingConnectCheck.Open || !await pending.TryConsumeAsync(sign.Id, now, cancellationToken))
        {
            var reason = check == PendingConnectCheck.Expired ? "the sign-in expired" : "the sign-in was used already";
            if (check == PendingConnectCheck.Expired)
            {
                await pending.TryConsumeAsync(sign.Id, now, cancellationToken);
            }

            return await FailAsync(sign.AccountId, null, reason, ConnectionErrors.ConnectExpiredCode, command.Actor, now, cancellationToken);
        }

        var account = await accounts.FindAsync(sign.AccountId, cancellationToken);
        if (account is null)
        {
            return Refused(AccountErrors.NotFoundCode, "the account no longer exists");
        }

        if (!SecretValue.TryCreate(command.Code, out var code, out _))
        {
            return await FailAsync(account, null, "Sign-in at Dhan was cancelled or returned no tokenId", ConnectionErrors.ConnectCancelledCode, command.Actor, now, cancellationToken);
        }

        var app = await writer.AppAsync(account.Id, cancellationToken);
        if (app is null)
        {
            return await FailAsync(account, null, "The API key or secret is not set", ConnectionErrors.AppNotConfiguredCode, command.Actor, now, cancellationToken);
        }

        var exchanged = await connector.CompleteLoginAsync(new LoginCompletion(app, code), cancellationToken);
        if (!exchanged.IsSuccess)
        {
            var failure = exchanged.Failure!;
            return await FailAsync(account, failure.BrokerCode, "Sign-in failed: " + ConnectionTexts.Failure(failure), ConnectionErrors.BrokerErrorCode, command.Actor, now, cancellationToken);
        }

        var token = exchanged.Value;
        if (!account.IsClientId(token.BrokerUserId))
        {
            return await FailAsync(account, null, "The Dhan sign-in belongs to another client id; nothing was stored", ConnectionErrors.ConnectMismatchCode, command.Actor, now, cancellationToken);
        }

        var connection = await writer.StoreAsync(token, account.Id, now, cancellationToken);
        events.Record(AccountEvent.Record(AccountEventType.BrokerConnected, account.Id, ConnectionTexts.Connected(connection), command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Success(new ConnectReturn(account.Id, AccountErrors.ConflictCode));
        }

        AccountsLog.Connected(logger, account.Slug);
        await attention.ReconcileAsync(cancellationToken); // the "reconnect" item resolves now, not at the next tick (ADR 0030)
        return Result.Success(new ConnectReturn(account.Id, null));
    }

    private Result<ConnectReturn> Refused(string code, string outcome)
    {
        AccountsLog.CallbackRefused(logger, outcome);
        return Result.Success(new ConnectReturn(null, code));
    }

    private async ValueTask<Result<ConnectReturn>> FailAsync(
        Guid accountId, string? brokerCode, string reason, string code, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(accountId, cancellationToken);
        return account is null
            ? Refused(code, reason)
            : await FailAsync(account, brokerCode, reason, code, actor, now, cancellationToken);
    }

    /// <summary>Notes the failure on the account (connection's last error, audit trail) and ends with the error code.</summary>
    private async ValueTask<Result<ConnectReturn>> FailAsync(
        BrokerAccount account, string? brokerCode, string reason, string code, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await writer.RecordFailureAsync(account.Id, reason, actor, now, cancellationToken);
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
        }

        AccountsLog.ConnectFailed(logger, account.Slug, code, brokerCode);
        return Result.Success(new ConnectReturn(account.Id, code));
    }
}

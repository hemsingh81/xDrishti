using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>
/// Starts the browser sign-in at Dhan (ADR 0008, 0009): Dhan generates a consent (counted against Dhan's daily limit). A pending sign-in
/// bound to the session (10 minutes, one use) waits for Dhan's callback; an older unused one of the session is closed.
/// </summary>
public sealed record StartConnectCommand(Guid AccountId, Guid SessionId, string Actor) : ICommand;

internal sealed class StartConnectHandler(
    IAccountStore accounts,
    IPendingConnectStore pending,
    IAccountUnitOfWork unitOfWork,
    ConnectionWriter writer,
    IBrokerConnector connector,
    TimeProvider clock,
    ILogger<StartConnectHandler> logger) : ICommandHandler<StartConnectCommand, ConnectStartView>
{
    /// <summary>Pending sign-ins are kept this long (Dhan's daily count needs today's), then deleted.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(2);

    public async ValueTask<Result<ConnectStartView>> HandleAsync(StartConnectCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<ConnectStartView>(AccountErrors.NotFound);
        }

        var app = await writer.AppAsync(account.Id, cancellationToken);
        if (app is null)
        {
            return Result.Failure<ConnectStartView>(ConnectionErrors.AppNotConfigured);
        }

        var now = clock.GetUtcNow();
        var (dayStart, dayEnd) = BrokerTokenExpiry.IndianDay(now);
        if (await pending.CountStartedAsync(account.Id, dayStart, dayEnd, cancellationToken) >= PendingConnect.DailyConsentLimit)
        {
            AccountsLog.ConnectFailed(logger, account.Slug, ConnectionErrors.ConsentLimitCode, null);
            return Result.Failure<ConnectStartView>(ConnectionErrors.ConsentLimit(PendingConnect.DailyConsentLimit, (int)Math.Ceiling((dayEnd - now).TotalSeconds)));
        }

        var started = await connector.StartLoginAsync(new LoginStartRequest(app, account.ClientId), cancellationToken);
        if (!started.IsSuccess)
        {
            var brokerCode = started.Failure!.BrokerCode;
            AccountsLog.ConnectFailed(logger, account.Slug, ConnectionErrors.BrokerErrorCode, brokerCode);
            return Result.Failure<ConnectStartView>(ConnectionErrors.Broker(started.Failure));
        }

        await pending.CloseUnusedAsync(command.SessionId, now, cancellationToken);
        await pending.PurgeAsync(now - Retention, cancellationToken);
        var sign = PendingConnect.Start(account.Id, command.SessionId, now);
        pending.Add(sign);
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<ConnectStartView>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.ConnectStarted(logger, account.Slug);
        return Result.Success(new ConnectStartView(started.Value.AbsoluteUri, sign.ExpiresAt));
    }
}

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Voids one trade (ADR 0023): it stops counting and is never deleted; its identity is free again, so the same trade can be imported again. Voiding a voided trade is a no-op. Changes no lots — rebuild them afterwards. Audited.
/// </summary>
public sealed record VoidTradeCommand(Guid TradeId, string Actor) : ICommand;

internal sealed class VoidTradeHandler(
    ITradeStore trades,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<VoidTradeHandler> logger) : ICommandHandler<VoidTradeCommand, TradeView>
{
    public async ValueTask<Result<TradeView>> HandleAsync(VoidTradeCommand command, CancellationToken cancellationToken)
    {
        var trade = await trades.FindAsync(command.TradeId, cancellationToken);
        if (trade is null)
        {
            return Result.Failure<TradeView>(PortfolioErrors.TradeNotFound);
        }

        var now = clock.GetUtcNow();
        if (trade.Void(command.Actor, now))
        {
            events.Record(AccountEvent.Record(AccountEventType.TradeVoided, trade.AccountId, $"Trade voided ({trade.Side.ToString().ToLowerInvariant()})", command.Actor, now));
            await unitOfWork.SaveAsync(cancellationToken);
            var account = await accounts.FindReadOnlyAsync(trade.AccountId, cancellationToken);
            TradesLog.Voided(logger, trade.Id, account?.Slug ?? "?");
        }

        return Result.Success(TradeViews.Trade(trade));
    }
}

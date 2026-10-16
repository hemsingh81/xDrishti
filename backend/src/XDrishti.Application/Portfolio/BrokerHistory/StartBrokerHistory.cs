using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// "Sync now" for the trades and ledger of a connected Dhan account (ADR 0025): checks the account, registers the trades run (<c>409 portfolio.sync_running</c> while a holdings sync or another broker history run of the account runs;
/// <c>409 portfolio.not_connected</c> or <c>portfolio.token_expired</c> without a usable Dhan token; <c>400 portfolio.invalid</c> for a start date in the future, before 1990-01-02 or later than the day after the dates already read, which would leave a hole), hands the run to the background worker and returns the
/// run as it begins. A first backfill takes minutes, so the caller reads the progress from the status. <c>From</c> is the first date to read instead of the watermark (default: the watermark, else the account's first import date).
/// </summary>
public sealed record StartBrokerHistoryCommand(Guid AccountId, DateOnly? From, string Actor) : ICommand;

internal sealed class StartBrokerHistoryHandler(BrokerHistoryAccess access, BrokerHistoryPipeline pipeline, IBrokerHistoryChannel queue) : ICommandHandler<StartBrokerHistoryCommand, BrokerHistoryRunView>
{
    public async ValueTask<Result<BrokerHistoryRunView>> HandleAsync(StartBrokerHistoryCommand command, CancellationToken cancellationToken)
    {
        var account = await access.AccountAsync(command.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<BrokerHistoryRunView>(account.Error);
        }

        var valid = await access.CheckFromAsync(account.Value, command.From, cancellationToken);
        if (valid.IsFailure)
        {
            return Result.Failure<BrokerHistoryRunView>(valid.Error);
        }

        var token = await access.TokenAsync(account.Value, cancellationToken);
        if (token.IsFailure)
        {
            return Result.Failure<BrokerHistoryRunView>(token.Error);
        }

        var begun = await pipeline.BeginAsync(account.Value, BrokerSyncKind.Trades, scheduled: false, command.From, command.Actor, cancellationToken);
        if (begun.IsFailure)
        {
            return Result.Failure<BrokerHistoryRunView>(begun.Error);
        }

        queue.Enqueue(new BrokerHistoryTicket(begun.Value.Id, account.Value.Id, command.From, command.Actor));
        return Result.Success(new BrokerHistoryRunView(BrokerHistoryViews.Run(begun.Value), null));
    }
}

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Reads the trades and then the ledger of a connected Dhan account and returns when both are done (ADR 0025): the scheduled service's use case, and the whole of "Sync now" for callers that wait (the API starts the run and lets a background worker
/// carry it on, see <see cref="StartBrokerHistoryCommand"/>). <c>From</c> is the first date to read instead of the watermark. <c>Scheduled</c> marks a run of the service: it is recorded as <c>scheduled</c> (or <c>first</c> for an account never read),
/// a scheduled run that cannot start because the token expired is recorded as skipped (<c>portfolio.token_expired</c>). One run per account at a time (<c>portfolio.sync_running</c>, also while a holdings sync runs).
/// </summary>
public sealed record SyncBrokerHistoryCommand(Guid AccountId, bool Scheduled, DateOnly? From, string Actor) : ICommand;

internal sealed class SyncBrokerHistoryHandler(BrokerHistoryAccess access, BrokerHistoryPipeline pipeline) : ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView>
{
    public async ValueTask<Result<BrokerHistoryRunView>> HandleAsync(SyncBrokerHistoryCommand command, CancellationToken cancellationToken)
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
            if (command.Scheduled && token.Error.Code == PortfolioErrors.TokenExpiredCode)
            {
                await pipeline.RecordSkippedAsync(account.Value, token.Error.Code, command.Actor, cancellationToken);
            }

            return Result.Failure<BrokerHistoryRunView>(token.Error);
        }

        var begun = await pipeline.BeginAsync(account.Value, BrokerSyncKind.Trades, command.Scheduled, command.From, command.Actor, cancellationToken);
        return begun.IsFailure
            ? Result.Failure<BrokerHistoryRunView>(begun.Error)
            : Result.Success(await pipeline.CarryOutAsync(account.Value, token.Value, begun.Value, command.From, command.Scheduled, command.Actor, cancellationToken));
    }
}

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Carries out a run that <see cref="StartBrokerHistoryCommand"/> registered (ADR 0025): the background worker's use case. Reads the trades run's state, the account and the token again - the token may have expired while the run waited - and carries
/// the run on (windows, automatic rebuild, then the ledger). A run that cannot go on - the account or token is gone, or anything throws, or the application stops - is ended as failed with the reason (completed with a token that cannot be cancelled), never left running. Nothing to do when the run is not running (any more).
/// </summary>
public sealed record ExecuteBrokerHistoryCommand(BrokerHistoryTicket Ticket) : ICommand;

internal sealed class ExecuteBrokerHistoryHandler(BrokerHistoryAccess access, BrokerHistoryPipeline pipeline, IBrokerSyncStore runs) : ICommandHandler<ExecuteBrokerHistoryCommand>
{
    public async ValueTask<Result> HandleAsync(ExecuteBrokerHistoryCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.Ticket;
        try
        {
            var run = await runs.GetAsync(ticket.RunId, cancellationToken);
            if (run is not { IsRunning: true })
            {
                return Result.Success();
            }

            var account = await access.AccountAsync(ticket.AccountId, cancellationToken);
            var token = account.IsSuccess ? await access.TokenAsync(account.Value, cancellationToken) : Result.Failure<Domain.Accounts.SecretValue>(account.Error);
            if (account.IsFailure || token.IsFailure)
            {
                await pipeline.EndAsync(run.Id, account.IsFailure ? account.Error : token.Error);
                return Result.Success();
            }

            await pipeline.CarryOutAsync(account.Value, token.Value, run, ticket.From, scheduled: false, ticket.Actor, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is stopping: the run ends as failed ("interrupted") now, so that the account is free at once; a ticket that never started is ended by the cleanup of interrupted syncs at the next start.
            await pipeline.InterruptedAsync(ticket.RunId);
            throw;
        }
#pragma warning disable CA1031 // whatever goes wrong, the run must not stay running (it would block the account); the worker goes on with the next run
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await pipeline.CrashedAsync(ticket.RunId, ex);
            return Result.Failure(PortfolioErrors.SyncFailed);
        }
    }
}

using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Undoes a dividend import (ADR 0028): voids every dividend of the batch (nothing is deleted; they stop counting in every figure) and marks the batch undone. Remembered names are not touched, and dividends the owner typed in are not the batch's. The same
/// file can be imported again afterwards. Undoing a batch again is a no-op. Audited with counts and the batch id only.
/// </summary>
public sealed record UndoDividendBatchCommand(Guid BatchId, string Actor) : ICommand;

internal sealed class UndoDividendBatchHandler(
    IDividendBatches batches,
    IDividendStore dividends,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UndoDividendBatchHandler> logger) : ICommandHandler<UndoDividendBatchCommand, DividendUndoResult>
{
    public async ValueTask<Result<DividendUndoResult>> HandleAsync(UndoDividendBatchCommand command, CancellationToken cancellationToken)
    {
        var found = await batches.GetAsync(command.BatchId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<DividendUndoResult>(PortfolioErrors.DividendBatchNotFound);
        }

        var account = await accounts.FindReadOnlyAsync(found.Batch.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<DividendUndoResult>(PortfolioErrors.AccountNotFound);
        }

        DividendUndoResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                var batch = await batches.FindAsync(command.BatchId, token);
                if (batch is null)
                {
                    return Result.Failure(PortfolioErrors.DividendBatchNotFound);
                }

                if (batch.Status == DividendBatchStatus.Undone)
                {
                    result = new DividendUndoResult(DividendImportViews.Batch((await batches.GetAsync(batch.Id, token))!, account.Name), AlreadyUndone: true, Voided: 0);
                    return Result.Success();
                }

                var now = clock.GetUtcNow();
                var voided = await dividends.VoidBatchAsync(batch.Id, command.Actor, now, token);
                batch.Undo(now);
                events.Record(AccountEvent.Record(AccountEventType.DividendsUndone, batch.AccountId, $"Dividend import {batch.Id} undone: {voided} dividends voided", command.Actor, now));
                await unitOfWork.SaveAsync(token);
                result = new DividendUndoResult(DividendImportViews.Batch((await batches.GetAsync(batch.Id, token))!, account.Name), AlreadyUndone: false, voided);
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<DividendUndoResult>(outcome.Error);
        }

        var done = result!;
        if (!done.AlreadyUndone)
        {
            DividendImportLog.Undone(logger, command.BatchId, account.Slug, done.Voided);
        }

        return Result.Success(done);
    }
}

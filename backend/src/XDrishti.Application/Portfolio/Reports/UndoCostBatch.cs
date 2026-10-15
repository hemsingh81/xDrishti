using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// Undoes an import of a depository-charges sheet (ADR 0029): voids every cost entry of the batch (nothing is deleted; they stop counting in every figure) and marks the batch undone. Names the sheet taught stay. The same sheet can be imported again afterwards. Undoing a batch
/// again is a no-op. Audited with counts and the batch id only.
/// </summary>
public sealed record UndoCostBatchCommand(Guid BatchId, string Actor) : ICommand;

internal sealed class UndoCostBatchHandler(
    ICostBatches batches,
    ILedgerStore ledger,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UndoCostBatchHandler> logger) : ICommandHandler<UndoCostBatchCommand, CostUndoResult>
{
    public async ValueTask<Result<CostUndoResult>> HandleAsync(UndoCostBatchCommand command, CancellationToken cancellationToken)
    {
        var found = await batches.GetAsync(command.BatchId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<CostUndoResult>(PortfolioErrors.CostBatchNotFound);
        }

        var account = await accounts.FindReadOnlyAsync(found.Batch.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<CostUndoResult>(PortfolioErrors.AccountNotFound);
        }

        CostUndoResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                var batch = await batches.FindAsync(command.BatchId, token);
                if (batch is null)
                {
                    return Result.Failure(PortfolioErrors.CostBatchNotFound);
                }

                if (batch.Status == CostBatchStatus.Undone)
                {
                    result = new CostUndoResult(CostViews.Batch((await batches.GetAsync(batch.Id, token))!, account.Name), AlreadyUndone: true, Voided: 0);
                    return Result.Success();
                }

                var now = clock.GetUtcNow();
                var voided = await ledger.VoidBatchAsync(batch.Id, command.Actor, now, token);
                batch.Undo(now);
                events.Record(AccountEvent.Record(AccountEventType.CostsUndone, batch.AccountId, $"Cost import {batch.Id} undone: {voided} entries voided", command.Actor, now));
                await unitOfWork.SaveAsync(token);
                result = new CostUndoResult(CostViews.Batch((await batches.GetAsync(batch.Id, token))!, account.Name), AlreadyUndone: false, voided);
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<CostUndoResult>(outcome.Error);
        }

        var done = result!;
        if (!done.AlreadyUndone)
        {
            ReportsLog.CostsUndone(logger, command.BatchId, account.Slug, done.Voided);
        }

        return Result.Success(done);
    }
}

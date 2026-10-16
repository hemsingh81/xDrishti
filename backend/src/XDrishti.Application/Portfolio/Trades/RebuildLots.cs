using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Rebuilds the lots of one holding from its stored trades (ADR 0023): with <c>DryRun</c> only says what it would do (the same <c>rebuild</c> block as the import preview, changing nothing); without, replaces the lots when the result is <c>ok</c>
/// (in one unit of work; a sync or quote that changed the holding meanwhile makes it run again once) and leaves them when it is <c>unchanged</c>, a <c>quantity_mismatch</c> or <c>ambiguous</c>. A holding without trades is rebuilt into one lot of the
/// broker's quantity and cost dated by the account's first import date, flagged as default. <c>409 portfolio.sync_running</c> while a holdings sync or a trades run of the account runs (not for a dry run; a ledger run does not stop it, ADR 0030). Audited with counts only.
/// </summary>
public sealed record RebuildLotsCommand(Guid HoldingId, bool DryRun, string Actor) : ICommand;

internal sealed class RebuildLotsHandler(
    TradeWorkspace workspace,
    IHoldingStore holdings,
    IAccountStore accounts,
    SyncGuard guard,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RebuildLotsHandler> logger) : ICommandHandler<RebuildLotsCommand, HoldingRebuildResult>
{
    public async ValueTask<Result<HoldingRebuildResult>> HandleAsync(RebuildLotsCommand command, CancellationToken cancellationToken)
    {
        var holding = await holdings.FindAsync(command.HoldingId, cancellationToken);
        var account = holding is null ? null : await accounts.FindReadOnlyAsync(holding.AccountId, cancellationToken);
        if (holding is null || account is null)
        {
            return Result.Failure<HoldingRebuildResult>(PortfolioErrors.HoldingNotFound);
        }

        if (command.DryRun)
        {
            var rebuild = await ComputeAsync(holding.AccountId, holding.Id, account.Portfolio.ImportDate, cancellationToken);
            return Result.Success(new HoldingRebuildResult(DryRun: true, Applied: false, TradeViews.Rebuild(rebuild)));
        }

        HoldingRebuild? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                if (await guard.RunningAsync(holding.AccountId, SyncWaits.TradeWrite, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunningWait);
                }

                var rebuild = await ComputeAsync(holding.AccountId, holding.Id, account.Portfolio.ImportDate, token);
                var now = clock.GetUtcNow();
                var applied = workspace.Apply([rebuild], now);
                if (applied.Holdings > 0)
                {
                    workspace.RecordRebuilt(holding.AccountId, applied, command.Actor, now);
                    await unitOfWork.SaveAsync(token);
                }

                result = rebuild;
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<HoldingRebuildResult>(outcome.Error);
        }

        if (result!.WillReplace)
        {
            TradesLog.Rebuilt(logger, command.HoldingId);
        }

        return Result.Success(new HoldingRebuildResult(DryRun: false, Applied: result.WillReplace, TradeViews.Rebuild(result)));
    }

    private ValueTask<HoldingRebuild> ComputeAsync(Guid accountId, Guid holdingId, DateOnly firstImportDate, CancellationToken cancellationToken) =>
        workspace.RebuildOfAsync(accountId, holdingId, firstImportDate, cancellationToken);
}

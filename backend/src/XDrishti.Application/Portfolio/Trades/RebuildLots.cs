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
/// broker's quantity and cost dated by the account's first import date, flagged as default. <c>409 portfolio.sync_running</c> while a holdings sync of the account runs (not for a dry run). Audited with counts only.
/// </summary>
public sealed record RebuildLotsCommand(Guid HoldingId, bool DryRun, string Actor) : ICommand;

internal sealed class RebuildLotsHandler(
    TradeWorkspace workspace,
    IHoldingStore holdings,
    ITradeStore trades,
    IAccountStore accounts,
    IHoldingSyncStore syncs,
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
                if (await syncs.IsRunningAsync(holding.AccountId, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
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

    /// <summary>Reads the account's holdings (tracked), trades and corporate actions and works out the rebuild of one holding; the holding is the tracked one, so applying the result changes what is saved.</summary>
    private async ValueTask<HoldingRebuild> ComputeAsync(Guid accountId, Guid holdingId, DateOnly firstImportDate, CancellationToken cancellationToken)
    {
        var all = await holdings.ListForAccountAsync(accountId, cancellationToken);
        var holding = all.Single(h => h.Id == holdingId);
        if (holding.Status != HoldingStatus.Open)
        {
            return HoldingRebuild.Without(RebuildStatuses.NoHolding, holding.Id.ToString("N"));
        }

        var matcher = new HoldingMatcher(all);
        if (matcher.Find(holding.Isin, holding.TradingSymbol) is not { Kind: MatchKind.One })
        {
            return HoldingRebuild.Without(RebuildStatuses.Ambiguous, holding.Id.ToString("N"));
        }

        // Only this security's trades are read, not the account's whole history.
        var candidates = await trades.ListActiveForSecurityAsync(accountId, TradeRules.NormaliseIsin(holding.Isin), TradeRules.NormaliseSymbol(holding.TradingSymbol), cancellationToken);
        var mine = HoldingRebuilds.Of(matcher, holding, candidates);
        var actions = await workspace.ActionsOfAsync([holding], cancellationToken);
        return HoldingRebuilds.Compute(holding, mine, holding.InstrumentId is { } instrument ? actions.GetValueOrDefault(instrument) ?? [] : [], firstImportDate);
    }
}

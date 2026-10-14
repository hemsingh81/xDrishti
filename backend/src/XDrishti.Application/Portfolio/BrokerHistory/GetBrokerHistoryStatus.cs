using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The status of the broker history sync of an account (ADR 0025): both kinds with their watermark, latest run and windows done of total, whether a run is running, when the service runs next (null when it is paused), and from the latest
/// <b>successful</b> trades run (and no other: no carry-forward from older runs) how many holdings were rebuilt automatically, how many need review and which (<c>NeedsReviewSymbols</c>, at most 50) - a run that was skipped (token expired) or failed rebuilt nothing and must not make the holdings that need review disappear from the status. Needs only that the account exists.
/// </summary>
public sealed record GetBrokerHistoryStatusQuery(Guid AccountId) : IQuery<Result<BrokerHistoryStatusView>>;

internal sealed class GetBrokerHistoryStatusHandler(IAccountStore accounts, IBrokerSyncStore runs, IServiceScheduleReader schedules)
    : IQueryHandler<GetBrokerHistoryStatusQuery, Result<BrokerHistoryStatusView>>
{
    public async ValueTask<Result<BrokerHistoryStatusView>> HandleAsync(GetBrokerHistoryStatusQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<BrokerHistoryStatusView>(AccountErrors.NotFound);
        }

        var trades = await KindAsync(account.Id, BrokerSyncKind.Trades, cancellationToken);
        var ledger = await KindAsync(account.Id, BrokerSyncKind.Ledger, cancellationToken);
        var schedule = await schedules.GetAsync(PortfolioBrokerHistoryService.Id, cancellationToken);
        var counts = (await runs.GetLastSucceededAsync(account.Id, BrokerSyncKind.Trades, cancellationToken))?.Counts;
        return Result.Success(new BrokerHistoryStatusView(
            await runs.IsRunningAsync(account.Id, cancellationToken),
            trades,
            ledger,
            account.Portfolio.Enabled ? schedule?.NextRunAt : null,
            counts?.Rebuilt ?? 0,
            counts?.NeedsReview ?? 0,
            NeedsReviewSymbols(counts)));
    }

    /// <summary>The symbols of the holdings that need review in the counts of the latest succeeded run, whatever status the latest run (of any status) is in; the counts list at most <see cref="BrokerSyncCounts.MaxNotRebuiltListed"/>.</summary>
    private static List<string> NeedsReviewSymbols(BrokerSyncCounts? counts) =>
        counts is null ? [] : [.. counts.NotRebuilt.Where(n => n.Reason == BrokerLotRebuilder.NeedsReview).Select(n => n.Symbol).Take(BrokerSyncCounts.MaxNotRebuiltListed)];

    private async ValueTask<BrokerKindStatus> KindAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken)
    {
        var watermark = await runs.GetWatermarkAsync(accountId, kind, cancellationToken);
        var last = await runs.GetLastAsync(accountId, kind, cancellationToken);
        var view = last is null ? null : BrokerHistoryViews.Run(last);
        return new BrokerKindStatus(watermark, view, view?.Counts.WindowsDone ?? 0, view?.Counts.WindowsTotal ?? 0);
    }
}

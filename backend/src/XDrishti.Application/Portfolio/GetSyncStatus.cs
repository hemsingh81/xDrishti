using System.Globalization;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The refresh settings of an account (mode, daily time, first import date, whether it has holdings), its latest run and when the next automatic run happens (null when the refresh is off or the Portfolio role is).</summary>
public sealed record GetSyncStatusQuery(Guid AccountId) : IQuery<Result<SyncStatusView>>;

internal sealed class GetSyncStatusHandler(IAccountStore accounts, IHoldingSyncStore syncs, IHoldingStore holdings, ISyncHealthStore health, IHealSettings settings, TimeProvider clock)
    : IQueryHandler<GetSyncStatusQuery, Result<SyncStatusView>>
{
    public async ValueTask<Result<SyncStatusView>> HandleAsync(GetSyncStatusQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<SyncStatusView>(AccountErrors.NotFound);
        }

        var refresh = account.Portfolio.Refresh;
        var activity = (await syncs.ActivityAsync([account.Id], cancellationToken)).GetValueOrDefault(account.Id) ?? new SyncActivity(null);
        var next = account.Portfolio.Enabled ? PortfolioRefreshSchedule.NextRun(clock.GetUtcNow(), refresh, activity) : null;
        var last = await syncs.GetLastAsync(account.Id, cancellationToken);
        var figures = await health.GetAsync(account.Id, SyncHealthKind.Holdings, cancellationToken);
        return Result.Success(new SyncStatusView(
            AccountCodes.Name(refresh.Mode),
            refresh.DailyTimeIst.ToString("HH:mm", CultureInfo.InvariantCulture),
            account.Portfolio.ImportDate,
            await holdings.AnyForAccountAsync(account.Id, cancellationToken),
            next,
            await syncs.IsRunningAsync(account.Id, cancellationToken),
            last is null ? null : HoldingViews.Run(last),
            figures.ConsecutiveFailedDays,
            figures.LastFailureReason,
            figures.Attempts,
            RetrySchedule.NextAttemptAt(figures, settings)));
    }
}

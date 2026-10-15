using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Heal;

/// <summary>
/// The self-healing of the account syncs (P2-16, ADR 0027), looked at once a minute by the holdings refresh runner: for every active account with the Portfolio role on,
/// <list type="number">
/// <item><b>Catch-up after a reconnect.</b> When the Dhan login was renewed after the latest failure of a kind that failed for want of one (<see cref="SyncFailures.IsLogin"/>), the missed holdings sync runs first and then the trade and ledger reads, by themselves.</item>
/// <item><b>Quiet retries.</b> A holdings, trade or ledger sync that failed for a reason that may pass (<see cref="SyncFailures.IsTransient"/>) is run again when the next wait of <see cref="IHealSettings.RetryBackoff"/> is over (<see cref="RetrySchedule"/>), inside the day of the failure. Nothing is shown while retries remain.</item>
/// </list>
/// The runs are the ordinary use cases (the same ones as "Sync now" and the schedules), so the usual rules hold: one run per account at a time (a retry that finds one running changes nothing and is tried at the next look), the same audit and the same rebuild. Failures of a run
/// are noted by the run itself; this class only decides when to start one. Accounts are handled one after the other.
/// </summary>
internal sealed class PortfolioHealer(
    IAccountStore accounts,
    IBrokerConnectionStore connections,
    ISyncHealthStore health,
    SyncHealthRecorder recorder,
    IHealSettings settings,
    ICommandHandler<SyncHoldingsCommand, SyncRunView> holdings,
    ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView> history,
    TimeProvider clock,
    ILogger<PortfolioHealer> logger)
{
    /// <summary>Recorded as the requester of the healing runs.</summary>
    public const string Requester = "system:portfolio-heal";

    /// <summary>One look at the accounts. Returns how many runs were started.</summary>
    public async ValueTask<int> TickAsync(CancellationToken cancellationToken)
    {
        var portfolio = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Portfolio.Enabled).ToList();
        if (portfolio.Count == 0)
        {
            return 0;
        }

        var ids = portfolio.Select(a => a.Id).ToList();
        var connected = await connections.ListAsync(ids, cancellationToken);
        var figures = (await health.ListAsync(ids, cancellationToken)).ToDictionary(h => (h.AccountId, h.Kind));
        var started = 0;
        foreach (var account in portfolio)
        {
            try
            {
                started += await HealAsync(account, connected.GetValueOrDefault(account.Id), figures, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // one account must not stop the healing of the others
            catch (Exception ex)
#pragma warning restore CA1031
            {
                HealLog.AccountCrashed(logger, account.Slug, ex);
            }
        }

        return started;
    }

    private async ValueTask<int> HealAsync(BrokerAccount account, BrokerConnection? connection, IReadOnlyDictionary<(Guid, SyncHealthKind), SyncHealth> figures, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        SyncHealth Of(SyncHealthKind kind) => figures.GetValueOrDefault((account.Id, kind)) ?? SyncHealth.None(account.Id, kind);
        var started = 0;

        // 1. The login was renewed after a sync failed for want of it: catch up, in the order of the nightly work - holdings first, then the trades and the ledger.
        if (connection is { LastConnectedAt: { } renewed } && connection.StatusAt(now) == ConnectionStatus.Connected)
        {
            var holdingsMissed = Missed(Of(SyncHealthKind.Holdings), renewed);
            var historyMissed = Missed(Of(SyncHealthKind.Trades), renewed) || Missed(Of(SyncHealthKind.Ledger), renewed);
            if (holdingsMissed || historyMissed)
            {
                HealLog.CatchingUp(logger, account.Slug, holdingsMissed && historyMissed ? "holdings, trades and ledger" : holdingsMissed ? "holdings" : "trades and ledger");
                if (holdingsMissed)
                {
                    await RunHoldingsAsync(account, h => Missed(h, renewed), cancellationToken);
                    started++;
                }

                if (historyMissed)
                {
                    await RunHistoryAsync(account, h => Missed(h, renewed), cancellationToken);
                    started++;
                }

                return started;
            }
        }

        // 2. A sync that failed for a reason that may pass is tried again when its wait is over.
        var holdingsHealth = Of(SyncHealthKind.Holdings);
        if (RetrySchedule.IsDue(holdingsHealth, settings, now))
        {
            HealLog.Retrying(logger, "holdings", account.Slug, holdingsHealth.Attempts + 1);
            await RunHoldingsAsync(account, h => RetrySchedule.IsDue(h, settings, clock.GetUtcNow()), cancellationToken);
            started++;
        }

        var tradesHealth = Of(SyncHealthKind.Trades);
        var ledgerHealth = Of(SyncHealthKind.Ledger);
        if (RetrySchedule.IsDue(tradesHealth, settings, now) || RetrySchedule.IsDue(ledgerHealth, settings, now))
        {
            var attempt = Math.Max(tradesHealth.Attempts, ledgerHealth.Attempts) + 1;
            HealLog.Retrying(logger, "trades and ledger", account.Slug, attempt);
            await RunHistoryAsync(account, h => RetrySchedule.IsDue(h, settings, clock.GetUtcNow()), cancellationToken);
            started++;
        }

        return started;
    }

    private async ValueTask RunHoldingsAsync(BrokerAccount account, Func<SyncHealth, bool> stillOwed, CancellationToken cancellationToken)
    {
        var result = await holdings.HandleAsync(new SyncHoldingsCommand(account.Id, SyncTrigger.Auto, Requester), cancellationToken);
        await NoteIfNotRecordedAsync(account.Id, [SyncHealthKind.Holdings], result.IsFailure ? result.Error : null, stillOwed, cancellationToken);
    }

    private async ValueTask RunHistoryAsync(BrokerAccount account, Func<SyncHealth, bool> stillOwed, CancellationToken cancellationToken)
    {
        var result = await history.HandleAsync(new SyncBrokerHistoryCommand(account.Id, Scheduled: true, From: null, Requester), cancellationToken);
        await NoteIfNotRecordedAsync(account.Id, [SyncHealthKind.Trades, SyncHealthKind.Ledger], result.IsFailure ? result.Error : null, stillOwed, cancellationToken);
    }

    /// <summary>
    /// A run that was refused before it began (the account is not connected after all, it has no bucket) notes nothing itself; if what made it due is still owed afterwards, the refusal is noted as the failure of the attempt so that the next look does not start
    /// the same thing again every minute. A run that found another one running, or the account still settling, is simply tried at the next look.
    /// </summary>
    private async ValueTask NoteIfNotRecordedAsync(Guid accountId, IReadOnlyList<SyncHealthKind> kinds, XDrishti.Domain.Common.Error? refusal, Func<SyncHealth, bool> stillOwed, CancellationToken cancellationToken)
    {
        if (refusal is not { } error || error.Code is PortfolioErrors.SyncRunningCode or PortfolioErrors.SettlingCode)
        {
            return;
        }

        foreach (var kind in kinds)
        {
            if (stillOwed(await health.GetAsync(accountId, kind, cancellationToken)))
            {
                await recorder.FailedAsync(accountId, kind, SyncFailures.Of(error), cancellationToken);
            }
        }
    }

    /// <summary>True when the kind's latest failure was for want of a login and the login was renewed after it.</summary>
    private static bool Missed(SyncHealth health, DateTimeOffset renewed) =>
        SyncFailures.IsLogin(health.LastFailureReason) && health.LastFailedAt is { } failedAt && failedAt < renewed;
}

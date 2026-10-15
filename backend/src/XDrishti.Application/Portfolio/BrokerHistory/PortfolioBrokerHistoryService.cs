using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The scheduled read of the trades and ledger of the Portfolio accounts as a service (<see cref="Id"/>, ADR 0025, ADR 0018): one run goes through every active account with the Portfolio role on, one after the other, and for each connected
/// Dhan account reads its trades and then its ledger (<see cref="SyncBrokerHistoryCommand"/>: a one-time backfill from the account's first import date, then incrementally from the watermark; lots are rebuilt automatically where that is safe).
/// <b>When</b> it runs is the scheduler's: by default every trading day at 16:30 India time, after the session and the end-of-day data run have started; Run now works at any time. An account whose Dhan token has expired is reported as
/// <c>token_expired</c> (the run fails so that the owner is told, the other accounts are still read); an account that is not connected is not touched; an account with a sync running is left for the next run. A deployment can start the service
/// paused (<c>Portfolio:BrokerHistory:Enabled = false</c>, a first-start seed). <b>A read that failed for a reason that may pass</b> (Dhan did not answer, rate limit; ADR 0027) is tried again quietly by the healing of the holdings refresh runner (1, 5 and 15 minutes) and is
/// not a failed run of the service: the message says "retrying" and the bell hears about it only when the reads have failed on <see cref="IHealSettings.PersistDays"/> days in a row. What only the owner can fix (the Dhan login) fails the run at once. The message holds counts and account names only.
/// </summary>
public sealed class PortfolioBrokerHistoryService(IServiceScopeFactory scopes, IBrokerHistorySettings settings, ILogger<PortfolioBrokerHistoryService> logger) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "portfolio-broker-history";

    /// <summary>The code an expired token is reported with.</summary>
    public const string TokenExpired = "token_expired";

    public const string NoAccountsMessage = "No Portfolio account is connected to Dhan";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Portfolio trades & ledger",
        "Reads the trades and the ledger (cash movements and dividends) of the Portfolio accounts from Dhan after the session, stores them next to the imported trades and rebuilds the lots of the holdings from the real dates where that is safe.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(16, 30)), 0, 60, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartNotBefore = new TimeOnly(15, 35),
            StartAdvice = "The session ends at 15:30; Dhan's trade history and ledger are complete for the day a little later.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary><c>Portfolio:BrokerHistory:Enabled</c>: read once, when the service has no stored row (ADR 0018 §3).</summary>
    public ServiceSettings? FirstStartSettings() => Definition.Defaults with { Enabled = settings.Enabled };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var automatic = context.Trigger != ServiceRunTrigger.Manual;
        var actor = automatic ? BrokerHistoryPipeline.SystemActor : context.RequestedBy;
        IReadOnlyList<BrokerAccount> accounts;
        await using (var listing = scopes.CreateAsyncScope())
        {
            accounts = [.. (await listing.ServiceProvider.GetRequiredService<IAccountStore>().ListActiveAsync(cancellationToken)).Where(a => a.Portfolio.Enabled)];
        }

        var read = 0;
        var busy = 0;
        var problems = new List<string>();
        var retrying = new List<string>();
        var stored = 0;
        var entries = 0;
        var rebuilt = 0;
        var review = 0;
        foreach (var account in accounts)
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView>>();
            var result = await handler.HandleAsync(new SyncBrokerHistoryCommand(account.Id, automatic, null, actor), cancellationToken);
            if (result.IsFailure)
            {
                switch (result.Error.Code)
                {
                    case PortfolioErrors.NotConnectedCode:
                        break; // not an account the service handles
                    case PortfolioErrors.SyncRunningCode:
                        busy++;
                        BrokerHistoryLog.ScheduledSkipped(logger, account.Slug, "a sync is running");
                        break;
                    case PortfolioErrors.TokenExpiredCode:
                        problems.Add($"{TokenExpired}: {account.Name}");
                        break;
                    default:
                        problems.Add($"failed: {account.Name}");
                        break;
                }

                continue;
            }

            var view = result.Value;
            var failed = view.Trades.Status == "failed" ? view.Trades : view.Ledger is { Status: "failed" } ? view.Ledger : null;
            if (failed is not null)
            {
                if (await IsQuietAsync(scope.ServiceProvider, account.Id, failed, cancellationToken))
                {
                    retrying.Add(account.Name);
                    continue;
                }

                problems.Add($"{(failed.Error == PortfolioErrors.TokenRefused.Message ? TokenExpired : "failed")}: {account.Name}");
                continue;
            }

            read++;
            stored += view.Trades.Counts.Stored;
            entries += view.Ledger?.Counts.Stored ?? 0;
            rebuilt += view.Trades.Counts.Rebuilt;
            review += view.Trades.Counts.NeedsReview;
        }

        BrokerHistoryLog.ScheduledDone(logger, read, accounts.Count, problems.Count);
        if (read + busy + problems.Count + retrying.Count == 0)
        {
            return ServiceOutcome.Skipped(NoAccountsMessage);
        }

        var message = Describe(read, busy, stored, entries, rebuilt, review, problems);
        if (retrying.Count > 0)
        {
            message += (message.Length > 0 ? "; " : string.Empty) + "retrying: " + string.Join(", ", retrying);
        }

        return problems.Count > 0 ? ServiceOutcome.Failed(message) : ServiceOutcome.Succeeded(message);
    }

    /// <summary>
    /// True when the failed run is one the healing deals with quietly: it failed for a reason that may pass and the reads have not yet failed on enough days in a row to tell the owner. A reason only the owner can fix, or one that keeps happening, is not quiet.
    /// </summary>
    private static async ValueTask<bool> IsQuietAsync(IServiceProvider services, Guid accountId, BrokerSyncRunView failed, CancellationToken cancellationToken)
    {
        var reason = failed.Counts.FailureReason;
        if (!SyncFailures.IsTransient(reason))
        {
            return false;
        }

        var kind = failed.Kind == "ledger" ? SyncHealthKind.Ledger : SyncHealthKind.Trades;
        var health = await services.GetRequiredService<ISyncHealthStore>().GetAsync(accountId, kind, cancellationToken);
        return health.ConsecutiveFailedDays < services.GetRequiredService<IHealSettings>().PersistDays;
    }

    /// <summary>"2 accounts read: 41 new trades, 12 new ledger entries, 2 holdings rebuilt, 1 need review; token_expired: Example One".</summary>
    public static string Describe(int read, int busy, int trades, int entries, int rebuilt, int review, IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var parts = new List<string>();
        if (read > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{read} {(read == 1 ? "account" : "accounts")} read: {trades} new trades, {entries} new ledger entries, {rebuilt} holdings rebuilt, {review} need review"));
        }

        if (busy > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{busy} left for the next run (a sync was running)"));
        }

        parts.AddRange(problems);
        return string.Join("; ", parts);
    }
}

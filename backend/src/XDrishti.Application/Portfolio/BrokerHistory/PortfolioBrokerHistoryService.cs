using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The scheduled read of the trades and ledger of the Portfolio accounts as a service (<see cref="Id"/>, ADR 0025, ADR 0018): one run goes through every active account with the Portfolio role on, one after the other, and for each connected
/// Dhan account reads its trades and then its ledger (<see cref="SyncBrokerHistoryCommand"/>: a one-time backfill from the account's first import date, then incrementally from the watermark; lots are rebuilt automatically where that is safe).
/// <b>When</b> it runs is the scheduler's: by default every trading day at 16:30 India time, after the session and the end-of-day data run have started; Run now works at any time. An account whose Dhan token has expired is reported as
/// <c>token_expired</c> (the run fails so that the owner is told, the other accounts are still read); an account that is not connected is not touched; an account with a sync running is left for the next run. A deployment can start the service
/// paused (<c>Portfolio:BrokerHistory:Enabled = false</c>, a first-start seed). The message holds counts and account names only.
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
        if (read + busy + problems.Count == 0)
        {
            return ServiceOutcome.Skipped(NoAccountsMessage);
        }

        var message = Describe(read, busy, stored, entries, rebuilt, review, problems);
        return problems.Count > 0 ? ServiceOutcome.Failed(message) : ServiceOutcome.Succeeded(message);
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

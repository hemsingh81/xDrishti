using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Portfolio.Quotes;

/// <summary>
/// The quote poll of the open holdings as a service (<see cref="Id"/>, ADR 0019, ADR 0018, price alerts ADR 0022): one run reads which securities the open holdings hold, asks the quote source (Dhan's market-quote API with the primary
/// Data account) for the ones that Dhan lists on the NSE, and writes last price, previous close and the fetch time into every open holding of those securities across accounts, in one statement. Holdings that
/// are not on the NSE (BSE only, retired, not in the master list) are not quoted and keep their synced price; the message counts them ("Quoted 41 of 43 holdings; 2 not on NSE" — securities, so a stock held in
/// two accounts counts once). Without open holdings the run succeeds and makes no request; without a usable Data account it is skipped with the reason; a failed request ends the run failed with the source's safe
/// message and changes no price (the next tick tries again — retries are 0). <b>When</b> it runs is the scheduler's: by default every 5 minutes from 09:15 to 15:30 on trading days (the last run is the closing price);
/// Run now works at any time. <b>Price alerts</b> (ADR 0022): the securities of the active alerts join the same request (still at most 1,000 per call); after the holdings are updated, every alert whose condition holds on the quoted last price is triggered
/// (once, in the transaction of its audit event; an alert without a quote stays active; a failed run triggers nothing) and the message adds "N alerts triggered"; then the Action center's done decisions whose condition ended are cleared.
/// Nothing secret is logged or put into a message.
/// </summary>
public sealed class HoldingQuotesService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<HoldingQuotesService> logger) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "holdings-quotes";

    public const string NoHoldingsMessage = "No open holdings";

    public const string NoUsableQuotesMessage = "Dhan's answer held no usable price.";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Holdings quotes",
        "Fetches the last price and the previous close of the open holdings from Dhan every few minutes during the session, for the prices and the day change on the Portfolio screen.",
        "xd-api",
        [ScheduleType.Interval],
        new ServiceSettings(true, Schedule.Interval(5, new TimeOnly(9, 15), new TimeOnly(15, 30)), 0, 2, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { MustCover = TimeRange.MarketHours });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    /// <summary>"Quoted 41 of 43 holdings; 2 not on NSE", plus "; 1 without a quote" when Dhan left a security out of its answer.</summary>
    public static string Describe(int quoted, int total, int notOnNse, int withoutQuote)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"Quoted {quoted} of {total} holdings; {notOnNse} not on NSE");
        return withoutQuote > 0 ? string.Create(CultureInfo.InvariantCulture, $"{text}; {withoutQuote} without a quote") : text;
    }

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var holdings = services.GetRequiredService<IHoldingQuotes>();
        var alerts = services.GetRequiredService<PriceAlertChecker>();
        var targets = await holdings.ListTargetsAsync(cancellationToken);

        // The active price alerts join the quoted set (ADR 0022); one that Dhan cannot quote (no NSE equity id) stays active and is never asked for.
        var active = await alerts.ListActiveAsync(cancellationToken);
        var quotableAlerts = active.Where(a => a.SecurityId is not null).ToList();
        if (targets.OpenSecurities == 0 && quotableAlerts.Count == 0)
        {
            HoldingQuotesLog.NoHoldings(logger);
            return ServiceOutcome.Succeeded(NoHoldingsMessage);
        }

        var notOnNse = targets.OpenSecurities - targets.Nse.Count;
        if (targets.Nse.Count == 0 && quotableAlerts.Count == 0)
        {
            HoldingQuotesLog.Quoted(logger, 0, targets.OpenSecurities, notOnNse, 0, 0, 0);
            return ServiceOutcome.Succeeded(Describe(0, targets.OpenSecurities, notOnNse, 0));
        }

        var account = await services.GetRequiredService<IDataAccountStatus>().GetAsync(cancellationToken);
        if (!account.CanFetch)
        {
            HoldingQuotesLog.Skipped(logger, "data_account_unavailable");
            return ServiceOutcome.Skipped(account.Reason ?? "There is no connected primary Data account.");
        }

        var byInstrument = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var target in targets.Nse)
        {
            byInstrument.TryAdd(target.SecurityId, target.InstrumentId);
        }

        var held = byInstrument.Count;
        var heldInstruments = targets.Nse.Select(t => t.InstrumentId).ToHashSet();
        foreach (var alert in quotableAlerts)
        {
            byInstrument.TryAdd(alert.SecurityId!, alert.InstrumentId);
        }

        var result = await services.GetRequiredService<IQuoteSource>().QuoteAsync([.. byInstrument.Keys], cancellationToken);
        if (result.Failure is { } failure)
        {
            // A failed run changes no price and triggers no alert.
            HoldingQuotesLog.Failed(logger, failure.Kind.ToString().ToLowerInvariant(), result.Requests);
            return ServiceOutcome.Failed(failure.Message);
        }

        var quotes = result.Quotes
            .Where(q => q.LastPrice > 0m && byInstrument.ContainsKey(q.SecurityId))
            .DistinctBy(q => q.SecurityId, StringComparer.Ordinal)
            .Select(q => new HoldingQuote(byInstrument[q.SecurityId], q.LastPrice, q.PreviousClose))
            .ToList();
        if (quotes.Count == 0)
        {
            HoldingQuotesLog.Failed(logger, "no_usable_price", result.Requests);
            return ServiceOutcome.Failed(NoUsableQuotesMessage);
        }

        var at = clock.GetUtcNow();
        var heldQuotes = quotes.Where(q => heldInstruments.Contains(q.InstrumentId)).ToList();
        var updated = heldQuotes.Count == 0 ? 0 : await holdings.ApplyAsync(heldQuotes, at, cancellationToken);
        var withoutQuote = held - heldQuotes.Count;
        HoldingQuotesLog.Quoted(logger, heldQuotes.Count, targets.OpenSecurities, notOnNse, withoutQuote, updated, result.Requests);

        // The alerts are checked against the same answer, after the holdings were updated; then the done decisions whose condition the new prices ended are cleared.
        var triggered = await alerts.CheckAsync(active, quotes.ToDictionary(q => q.InstrumentId, q => q.LastPrice), at, cancellationToken);
        await services.GetRequiredService<ActionReconciler>().ReconcileAsync("quotes", cancellationToken);
        var message = targets.OpenSecurities == 0 ? NoHoldingsMessage : Describe(heldQuotes.Count, targets.OpenSecurities, notOnNse, withoutQuote);
        return ServiceOutcome.Succeeded(triggered > 0 ? message + "; " + DescribeTriggered(triggered) : message);
    }

    /// <summary>"1 alert triggered" · "3 alerts triggered".</summary>
    public static string DescribeTriggered(int triggered) => string.Create(CultureInfo.InvariantCulture, $"{triggered} {(triggered == 1 ? "alert" : "alerts")} triggered");
}

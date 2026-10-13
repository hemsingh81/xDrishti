using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Portfolio.Quotes;

/// <summary>
/// The quote poll of the open holdings as a service (<see cref="Id"/>, ADR 0019, ADR 0018): one run reads which securities the open holdings hold, asks the quote source (Dhan's market-quote API with the primary
/// Data account) for the ones that Dhan lists on the NSE, and writes last price, previous close and the fetch time into every open holding of those securities across accounts, in one statement. Holdings that
/// are not on the NSE (BSE only, retired, not in the master list) are not quoted and keep their synced price; the message counts them ("Quoted 41 of 43 holdings; 2 not on NSE" — securities, so a stock held in
/// two accounts counts once). Without open holdings the run succeeds and makes no request; without a usable Data account it is skipped with the reason; a failed request ends the run failed with the source's safe
/// message and changes no price (the next tick tries again — retries are 0). <b>When</b> it runs is the scheduler's: by default every 5 minutes from 09:15 to 15:30 on trading days (the last run is the closing price);
/// Run now works at any time. Nothing secret is logged or put into a message.
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
        var targets = await holdings.ListTargetsAsync(cancellationToken);
        if (targets.OpenSecurities == 0)
        {
            HoldingQuotesLog.NoHoldings(logger);
            return ServiceOutcome.Succeeded(NoHoldingsMessage);
        }

        var notOnNse = targets.OpenSecurities - targets.Nse.Count;
        if (targets.Nse.Count == 0)
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

        var result = await services.GetRequiredService<IQuoteSource>().QuoteAsync([.. byInstrument.Keys], cancellationToken);
        if (result.Failure is { } failure)
        {
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

        var updated = await holdings.ApplyAsync(quotes, clock.GetUtcNow(), cancellationToken);
        var withoutQuote = byInstrument.Count - quotes.Count;
        HoldingQuotesLog.Quoted(logger, quotes.Count, targets.OpenSecurities, notOnNse, withoutQuote, updated, result.Requests);
        return ServiceOutcome.Succeeded(Describe(quotes.Count, targets.OpenSecurities, notOnNse, withoutQuote));
    }
}

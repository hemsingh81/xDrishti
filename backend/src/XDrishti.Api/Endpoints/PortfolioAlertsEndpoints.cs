using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// One-shot price alerts on NSE shares and ETFs (ADR 0022, P2-09). They are checked by the quotes service on every tick against the last price of the tick (every 5 minutes while the market is open; a spike between two ticks is not seen). Reading needs a session;
/// creating and cancelling also need the CSRF token; none needs a step-up. Nothing is ever deleted.
/// </summary>
internal sealed class PortfolioAlertsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/portfolio/alerts").WithTags("Portfolio");

        group.MapGet("/", ListAsync)
            .WithName("ListPortfolioAlerts")
            .WithSummary("The price alerts, newest first: status active | triggered | cancelled | all (default all), page, pageSize (max 100, default 50). Each alert has id, instrumentId, symbol, name, direction (at_or_below | at_or_above), level, note, status, createdAt, triggeredAt and triggerPrice (the last price of the tick that hit the level). The page also carries activeCount of activeLimit (200)")
            .ProducesValidationProblem();

        group.MapPost("/", CreateAsync)
            .WithName("CreatePortfolioAlert")
            .WithSummary("Create an active alert: body {instrumentId (an NSE share or ETF of the instrument master that is not retired and that Dhan can quote), direction at_or_below | at_or_above, level (above 0, at most 10,000,000, at most 4 decimals), note (optional, at most 200 characters of plain text)}. Checked on each quotes tick's last price (at_or_below: last <= level; at_or_above: last >= level). 400 portfolio.invalid with field errors, including field level when the condition already holds at the last known price (the holding's last price, else the last stored daily close; unknown: allowed) with the price in the message; 409 portfolio.alert_limit at 200 active alerts. Audited")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelPortfolioAlert")
            .WithSummary("Cancel an alert. Idempotent: a cancelled alert stays cancelled and a triggered one stays triggered (the answer shows the real status). Audited when it cancels. 404 portfolio.not_found for an unknown id")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<PriceAlertPage>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListPriceAlertsQuery, Result<PriceAlertPage>> handler,
        CancellationToken cancellationToken,
        string? status = null,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListPriceAlertsQuery(status, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<PriceAlertView>, ProblemHttpResult>> CreateAsync(
        PriceAlertInput input,
        HttpContext http,
        ICommandHandler<CreatePriceAlertCommand, PriceAlertView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new CreatePriceAlertCommand(input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<PriceAlertView>, ProblemHttpResult>> CancelAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<CancelPriceAlertCommand, PriceAlertView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new CancelPriceAlertCommand(id, http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}

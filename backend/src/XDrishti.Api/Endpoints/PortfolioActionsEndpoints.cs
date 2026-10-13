using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The Action center of the Portfolio screen (ADR 0022, P2-09): the items the seven rules produce (evaluated on read), the owner's decisions about them, the thresholds of the rules, and the Portfolio notices of the bell. Reading needs a session; changes also need
/// the CSRF token; none needs a step-up (the owner's own decisions about public portfolio data). Percentages are percent numbers (23.4 = 23.4 %).
/// </summary>
internal sealed class PortfolioActionsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/portfolio").WithTags("Portfolio");

        group.MapGet("/actions", GetActionsAsync)
            .WithName("GetPortfolioActions")
            .WithSummary("The holdings that need a decision, evaluated on read by seven rules (below_cost, large_gain, overweight, sector_concentration, long_term_soon, lagging_nifty, not_in_basket): items[{id, rule, severity high|medium|info, accountId, accountName, holdingId, instrumentId, symbol (null for the account-level sector_concentration), title, detail, numbers (the figures behind the item; which are set depends on the rule), suggestions[open|set_alert|add_to_basket|snooze|done], state open|snoozed|done, snoozedUntil}] most important first (severity, then the size of the trigger), and counts {open, snoozed, done, byRule, bySeverity (of the open items; every rule and severity present), unavailable {lagging: holdings held long enough for lagging_nifty that could not be judged because the benchmark has no bars from the first flow or an XIRR is not ok}, noPrice (open holdings without a usable price: no price-based rule fires for them)}. Filters: accountId (404 accounts.not_found when unknown; without it the accounts that count in totals), bucketId (the items about its holdings; weights and sector shares stay shares of the whole account), status open (default) | snoozed | done | all (the counts always cover the whole scope). A snoozed item shows again on its snoozedUntil date. One write is made by this read: a done decision whose condition no longer holds is cleared, so a recurrence shows again")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/actions/{id}/snooze", SnoozeAsync)
            .WithName("SnoozePortfolioAction")
            .WithSummary("Hide an item for 7 days (it shows again on that India date if a rule still produces it): answers {id, state, snoozedUntil, item}. Idempotent; audited. 404 portfolio.not_found for an id that no rule produces now (stale)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/actions/{id}/done", DoneAsync)
            .WithName("MarkPortfolioActionDone")
            .WithSummary("Mark an item done: it stays hidden for as long as the condition holds and the decision is cleared when it stops holding (a later recurrence is open again). Answers {id, state, snoozedUntil, item}. Idempotent; audited. 404 portfolio.not_found for an id that no rule produces now (stale)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/actions/{id}/reopen", ReopenAsync)
            .WithName("ReopenPortfolioAction")
            .WithSummary("Take a snooze or done decision back: the item is open again. Answers {id, state open, item (null when no rule produces the id now)}. Idempotent; audited when it changes something; allowed for an item that is gone; 404 portfolio.not_found only for an id that is not the id of any rule's item")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/action-thresholds", GetThresholdsAsync)
            .WithName("GetPortfolioActionThresholds")
            .WithSummary("The thresholds of the Action-center rules in force: values {belowCostPct, belowCostHighPct, gainPct, overweightFloorPct, overweightMultiple, minPositions, concentrationPct, lagPp, lagDays, longTermWindowDays}, the documented defaults, isDefault, updatedBy and updatedAt (null while none are stored)");

        group.MapPut("/action-thresholds", SaveThresholdsAsync)
            .WithName("SavePortfolioActionThresholds")
            .WithSummary("Save the thresholds (all ten required): below cost 1-90 %, high 2-95 % and above below cost, gain 1-1000 %, overweight floor 1-100 %, multiple 1-10, minimum positions 2-50, concentration 5-100 %, lag 1-100 points, lag days 30-3650, long-term window 1-365 days; at most 2 decimals, minPositions/lagDays/longTermWindowDays whole numbers. 400 portfolio.invalid with a field error per problem. The next read of the actions uses them at once. Audited")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapPost("/action-thresholds/reset", ResetThresholdsAsync)
            .WithName("ResetPortfolioActionThresholds")
            .WithSummary("Restore defaults: writes the documented default thresholds. Audited; nothing changes when they are already in force")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/notices", GetNoticesAsync)
            .WithName("GetPortfolioNotices")
            .WithSummary("What the owner should know about the portfolio, for the notification bell: price_alert_triggered (warn; one per alert, id portfolio:alert:{alertId}, listed for 7 days after it triggered; carries alertId and instrumentId) and actions_need_decision (info; \"N holdings need a decision\" when the accounts that count in totals have open Action-center items; id portfolio:actions:{India date}, so it is raised again at most once a day)");
    }

    private static async Task<Results<Ok<ActionList>, ProblemHttpResult>> GetActionsAsync(
        HttpContext http,
        IQueryHandler<GetActionItemsQuery, Result<ActionList>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null,
        string? status = null) =>
        Respond(await handler.HandleAsync(new GetActionItemsQuery(new ActionItemsRequest(accountId, bucketId, status)), cancellationToken), http);

    private static async Task<Results<Ok<ActionDecisionView>, ProblemHttpResult>> SnoozeAsync(
        string id,
        HttpContext http,
        ICommandHandler<SnoozeActionCommand, ActionDecisionView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new SnoozeActionCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<ActionDecisionView>, ProblemHttpResult>> DoneAsync(
        string id,
        HttpContext http,
        ICommandHandler<MarkActionDoneCommand, ActionDecisionView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new MarkActionDoneCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<ActionDecisionView>, ProblemHttpResult>> ReopenAsync(
        string id,
        HttpContext http,
        ICommandHandler<ReopenActionCommand, ActionDecisionView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new ReopenActionCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Ok<ActionThresholdsView>> GetThresholdsAsync(IQueryHandler<GetActionThresholdsQuery, ActionThresholdsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetActionThresholdsQuery(), cancellationToken));

    private static async Task<Results<Ok<ActionThresholdsView>, ProblemHttpResult>> SaveThresholdsAsync(
        ActionThresholdsInput input,
        HttpContext http,
        ICommandHandler<SaveActionThresholdsCommand, ActionThresholdsView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new SaveActionThresholdsCommand(input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<ActionThresholdsView>, ProblemHttpResult>> ResetThresholdsAsync(
        HttpContext http,
        ICommandHandler<ResetActionThresholdsCommand, ActionThresholdsView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new ResetActionThresholdsCommand(http.GetActor()), cancellationToken), http);

    private static async Task<Ok<PortfolioNotices>> GetNoticesAsync(IQueryHandler<GetPortfolioNoticesQuery, PortfolioNotices> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetPortfolioNoticesQuery(), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}

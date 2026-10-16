using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Dividends;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Dividends the owner received (ADR 0026, P2-14): adding and voiding one, the dividends of a holding and of an account, and the suggested dividends from the ledger with accept and dismiss. All routes need a session; every POST also needs the CSRF token;
/// none needs a step-up (no secrets are involved; a mistake is voided, nothing is deleted).
/// </summary>
internal sealed class PortfolioDividendsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var dividends = api.MapGroup("/portfolio/dividends").WithTags("Dividends");

        dividends.MapPost("/", AddAsync)
            .WithName("AddDividend")
            .WithSummary("Add a dividend you received: holdingId (the account is then the holding's) or accountId alone (unallocated: no holding); payDate (1990-01-02 to today, India), amount (rupees, above 0), perShare (optional, above 0), note (optional plain text, at most 200 characters). The same account, holding, date and amount as a dividend that is not voided is a possible duplicate: 409 portfolio.possible_duplicate, repeat with confirm true to add the second payment. 400 portfolio.invalid with field errors; 404 portfolio.not_found (account); 409 portfolio.role_off. Audited with the id only, never the amount")
            .ProducesRejections(conflict: true);

        dividends.MapPost("/{id:guid}/void", VoidAsync)
            .WithName("VoidDividend")
            .WithSummary("Void a dividend: it stops counting in the figures and is never deleted. Voiding a voided dividend is a no-op. 404 portfolio.not_found. Audited with the id only")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var holding = api.MapGroup("/holdings/{id:guid}").WithTags("Dividends");

        holding.MapGet("/dividends", ListHoldingAsync)
            .WithName("ListHoldingDividends")
            .WithSummary("The dividends entered for a holding, newest first (pay date), voided ones included and flagged (voided, voidedAt, voidedBy): page, pageSize (max 200, default 50)")
            .ProducesRejections(conflict: false);

        var account = api.MapGroup("/accounts/{id:guid}/dividends").WithTags("Dividends");

        account.MapGet("/", ListAccountAsync)
            .WithName("ListAccountDividends")
            .WithSummary("The dividends entered for an account - every holding's and the unallocated ones (holdingId null) - newest first, voided ones included and flagged: page, pageSize (max 200, default 50)")
            .ProducesRejections(conflict: false);

        account.MapGet("/suggestions", ListSuggestionsAsync)
            .WithName("ListDividendSuggestions")
            .WithSummary("The suggested dividends of an account: its ledger entries of class dividend that were not accepted or dismissed yet, newest first (the ledger entry shape: id, date, amount, description and narration - Dhan's free text, show as plain text): page, pageSize (max 200, default 50). Dhan's documented ledger has no entry for an ordinary dividend, so this is usually empty. Nothing is added without accepting")
            .ProducesRejections(conflict: false);

        account.MapPost("/suggestions/{entryId:guid}/accept", AcceptAsync)
            .WithName("AcceptDividendSuggestion")
            .WithSummary("Accept a suggested dividend: creates a dividend of the ledger entry's amount and date, for the holding you choose in the optional body {holdingId, note} (one of the account's) or unallocated, and remembers the decision (final; voiding the dividend later does not bring the suggestion back). 404 portfolio.not_found (not a dividend entry of the account); 409 portfolio.conflict (decided meanwhile), portfolio.role_off. Audited with ids only")
            .ProducesRejections(conflict: true);

        account.MapPost("/suggestions/{entryId:guid}/dismiss", DismissAsync)
            .WithName("DismissDividendSuggestion")
            .WithSummary("Dismiss a suggested dividend: it is not offered again; nothing else changes. The decision is final. 404 portfolio.not_found (not a dividend entry of the account); 409 portfolio.conflict (decided meanwhile). Audited with ids only")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Results<Ok<DividendView>, ProblemHttpResult>> AddAsync(
        DividendInput input,
        HttpContext http,
        ICommandHandler<AddDividendCommand, DividendView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new AddDividendCommand(input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<DividendView>, ProblemHttpResult>> VoidAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<VoidDividendCommand, DividendView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new VoidDividendCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<DividendPage>, ProblemHttpResult>> ListHoldingAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListHoldingDividendsQuery, Result<DividendPage>> handler,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListHoldingDividendsQuery(id, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<DividendPage>, ProblemHttpResult>> ListAccountAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListAccountDividendsQuery, Result<DividendPage>> handler,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListAccountDividendsQuery(id, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<DividendSuggestionPage>, ProblemHttpResult>> ListSuggestionsAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListDividendSuggestionsQuery, Result<DividendSuggestionPage>> handler,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null) =>
        Respond(await handler.HandleAsync(new ListDividendSuggestionsQuery(id, page, pageSize), cancellationToken), http);

    private static async Task<Results<Ok<DividendView>, ProblemHttpResult>> AcceptAsync(
        Guid id,
        Guid entryId,
        HttpContext http,
        ICommandHandler<AcceptDividendSuggestionCommand, DividendView> handler,
        CancellationToken cancellationToken,
        [FromBody] AcceptSuggestionInput? input = null) =>
        Respond(await handler.HandleAsync(new AcceptDividendSuggestionCommand(id, entryId, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<SuggestionDecisionView>, ProblemHttpResult>> DismissAsync(
        Guid id,
        Guid entryId,
        HttpContext http,
        ICommandHandler<DismissDividendSuggestionCommand, SuggestionDecisionView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new DismissDividendSuggestionCommand(id, entryId, http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}

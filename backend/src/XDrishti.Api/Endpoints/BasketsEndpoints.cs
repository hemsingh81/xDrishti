using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Baskets (P1-15): manual groups of instruments, many-to-many, with dated membership and order. All routes need a session; changes also need the CSRF
/// token, none needs a step-up. Part of the market module. The portfolio buckets also appear here as read-only system baskets (ADR 0014): every change
/// route answers 409 baskets.system for them.
/// </summary>
internal sealed class BasketsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/baskets").WithTags("Baskets");

        group.MapGet("/", ListAsync)
            .WithName("ListBaskets")
            .WithSummary("The baskets with their member counts: the system baskets first (kind portfolio: one per portfolio bucket, read-only, by account then name), then the owner's in their order (kind manual); archived manual ones only with includeArchived=true");

        group.MapPost("/", CreateAsync)
            .WithName("CreateBasket")
            .WithSummary("Create an empty basket (name, colour, purpose, description); 409 baskets.name_taken when an active basket has the name (ignoring case)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Literal segments win over the {id:guid} routes, and the guid constraint keeps "order" and "transfer" from ever matching them.
        group.MapPut("/order", ReorderAsync)
            .WithName("ReorderBaskets")
            .WithSummary("Put the baskets in this order: the ids must be every active basket exactly once (400 baskets.invalid), 409 baskets.conflict when baskets changed meanwhile")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/transfer", TransferAsync)
            .WithName("TransferBasketMembers")
            .WithSummary("Copy or move up to 500 instruments from one basket to another, atomically and idempotently (only members of the source are transferred; targets are tracked automatically); a system basket can be the source of a copy only, anything else with one is 409 baskets.system")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/system/fetch", FetchSystemAsync)
            .RequireStepUp()
            .WithName("FetchSystemBaskets")
            .WithSummary("Owner act, needs step-up: fetch the index lists and the two NSE F&O files (the ban list and the lot sizes) now (the given basketIds that are already on, or every enabled source; fetch-now never turns a source on) and return one outcome per file; force overrides only the tripwire and is recorded. The index lists need Baskets:System:Fetch:Enabled, the F&O files Baskets:System:Lists:FetchEnabled; a file whose setting is off is not requested. 409 baskets.fetch_disabled while both are off (no request is made)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/system/derived/refresh", RefreshDerivedAsync)
            .RequireStepUp()
            .WithName("RefreshDerivedBaskets")
            .WithSummary("Owner act, needs step-up: form a version of every enabled derived basket now, from our own daily bars, as of the last closed session (no network); one outcome per basket. 409 baskets.derived_disabled while Baskets:System:Derived:Enabled is off (nothing is read or written), 409 baskets.derived_running while a run is going")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/system/derived/backfill", BackfillDerivedAsync)
            .RequireStepUp()
            .WithName("BackfillDerivedBasket")
            .WithSummary("Owner act, needs step-up: recompute one derived basket for every due date of a closed range (at most MaxBackfillDays trading days) from bars cut at each date, as recomputed versions; never overwrites a published version. 202 with the run and the note that adjustments are today's restated series; 409 baskets.backfill_overlap when the range reaches the first published version, 409 baskets.derived_disabled while off")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/system/derived/{id:guid}/params", SetDerivedParamsAsync)
            .RequireStepUp()
            .WithName("SetDerivedBasketParams")
            .WithSummary("Owner act, needs step-up: store a new immutable parameter set (overrides of the defaults, checked against the engine's ranges) for a derived basket and select it, and/or switch the basket on or off; audited with the old and new hash. A basket this build cannot form (13, 4, 12) cannot be switched on (400 baskets.invalid)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/versions", GetVersionsAsync)
            .WithName("ListBasketVersions")
            .WithSummary("The published versions of a derived basket, newest first (skip, take 1 to 200, default 50): as_of, effectiveFrom (the next session), member and universe counts, the parameter hash and the provenance (derived or recomputed); 404 baskets.not_found for a basket without versions")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/versions/{versionId:guid}", GetVersionAsync)
            .WithName("GetBasketVersion")
            .WithSummary("One version of a derived basket: the members and the next non-members with score and rank, the named exclusions, the counts (members + below threshold + excluded = universe) and the parameters it was computed with")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/source", GetSourceAsync)
            .WithName("GetBasketSource")
            .WithSummary("The source of a system basket: file name, state (never, ok, stale, rejected, failed), last checks and its last 20 fetch outcomes (no raw file); 404 baskets.not_found for a basket without one")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/source", SwitchSourceAsync)
            .RequireStepUp()
            .WithName("SwitchBasketSource")
            .WithSummary("Owner act, needs step-up, both directions: turn the fetch of this system basket's file on or off ({enabled}); audited with the fetch settings, idempotent, makes no request; 400 baskets.invalid without enabled, 404 baskets.not_found for a basket without a source")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateBasket")
            .WithSummary("Replace the name, colour, purpose and description of a basket that is not archived; 409 baskets.system for a system basket")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", ArchiveAsync)
            .WithName("ArchiveBasket")
            .WithSummary("Archive a basket (idempotent): it leaves the list, its members and history stay readable, instruments stay tracked; 409 baskets.system for a system basket")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/members", GetMembersAsync)
            .WithName("ListBasketMembers")
            .WithSummary("The members of a basket in their order, with the instrument and its tracked block; asOf=yyyy-MM-dd (India date) gives the members at the end of that day. A system basket gives the instruments of the bucket's current holdings with quantity and accountName; asOf on it is 400 baskets.invalid")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/members", AddMembersAsync)
            .WithName("AddBasketMembers")
            .WithSummary("Add 1 to 500 instruments (idempotent, all or nothing); untracked ones are tracked in the same transaction, a retired one that is not tracked is refused (instruments.retired)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/members", RemoveMembersAsync)
            .WithName("RemoveBasketMembers")
            .WithSummary("Remove 1 to 500 instruments (idempotent, all or nothing); the stay stays in the history, the instruments stay tracked")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/members/order", ReorderMembersAsync)
            .WithName("ReorderBasketMembers")
            .WithSummary("Put the members in this order: the instrumentIds must be every member exactly once (400 baskets.invalid), 409 baskets.conflict when the basket changed meanwhile")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/history", GetHistoryAsync)
            .WithName("GetBasketHistory")
            .WithSummary("The newest additions and removals of a basket, newest first (take 1 to 1000, default 200); 400 baskets.invalid for a system basket")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGroup("/instruments").WithTags("Baskets")
            .MapGet("/{id:guid}/baskets", ListForInstrumentAsync)
            .WithName("ListInstrumentBaskets")
            .WithSummary("The baskets (not archived) that hold the instrument now")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<BasketView>>> ListAsync(
        IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>> handler,
        CancellationToken cancellationToken,
        bool? includeArchived = null) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBasketsQuery(includeArchived ?? false), cancellationToken));

    private static async Task<Results<Created<BasketView>, ProblemHttpResult>> CreateAsync(
        HttpContext http,
        ICommandHandler<CreateBasketCommand, BasketView> handler,
        CancellationToken cancellationToken,
        [FromBody] BasketInput? input = null)
    {
        var result = await handler.HandleAsync(new CreateBasketCommand(input ?? new BasketInput(null, null, null, null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Created($"/api/baskets/{result.Value.Id}", result.Value);
    }

    private static async Task<Results<Ok<BasketView>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<UpdateBasketCommand, BasketView> handler,
        CancellationToken cancellationToken,
        [FromBody] BasketInput? input = null)
    {
        var result = await handler.HandleAsync(new UpdateBasketCommand(id, input ?? new BasketInput(null, null, null, null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ArchiveAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<ArchiveBasketCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ArchiveBasketCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ReorderAsync(
        HttpContext http,
        ICommandHandler<ReorderBasketsCommand> handler,
        CancellationToken cancellationToken,
        [FromBody] BasketOrderInput? input = null)
    {
        var result = await handler.HandleAsync(new ReorderBasketsCommand(input ?? new BasketOrderInput(null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<BasketFetchRun>, ProblemHttpResult>> FetchSystemAsync(
        HttpContext http,
        ICommandHandler<RunBasketFetchCommand, BasketFetchRun> handler,
        CancellationToken cancellationToken,
        [FromBody] FetchNowInput? input = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new RunBasketFetchCommand(input?.BasketIds, input?.Force ?? false, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DerivedRun>, ProblemHttpResult>> RefreshDerivedAsync(
        HttpContext http,
        ICommandHandler<RefreshDerivedBasketsCommand, DerivedRun> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new RefreshDerivedBasketsCommand(http.GetActor(), true), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<DerivedRun>, ProblemHttpResult>> BackfillDerivedAsync(
        HttpContext http,
        ICommandHandler<BackfillDerivedBasketCommand, DerivedRun> handler,
        CancellationToken cancellationToken,
        [FromBody] DerivedBackfillInput? input = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new BackfillDerivedBasketCommand(input?.BasketId ?? Guid.Empty, input?.From, input?.To, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Accepted((string?)null, result.Value);
    }

    private static async Task<Results<Ok<DerivedDefinitionView>, ProblemHttpResult>> SetDerivedParamsAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<SetDerivedParamsCommand, DerivedDefinitionView> handler,
        CancellationToken cancellationToken,
        [FromBody] DerivedParamsInput? input = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new SetDerivedParamsCommand(id, input?.Overrides, input?.Enabled, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketVersionPage>, ProblemHttpResult>> GetVersionsAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetBasketVersionsQuery, Result<BasketVersionPage>> handler,
        CancellationToken cancellationToken,
        int skip = 0,
        int take = 50)
    {
        var result = await handler.HandleAsync(new GetBasketVersionsQuery(id, skip, take), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketVersionDetail>, ProblemHttpResult>> GetVersionAsync(
        Guid id,
        Guid versionId,
        HttpContext http,
        IQueryHandler<GetBasketScoresQuery, Result<BasketVersionDetail>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetBasketScoresQuery(id, versionId), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketSourceView>, ProblemHttpResult>> SwitchSourceAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<SetBasketSourceEnabledCommand, BasketSourceView> handler,
        CancellationToken cancellationToken,
        [FromBody] SourceSwitchInput? input = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (input?.Enabled is not { } enabled)
        {
            return ProblemResults.From(BasketErrors.Invalid("enabled", "Send enabled: true or false."), http);
        }

        var result = await handler.HandleAsync(new SetBasketSourceEnabledCommand(id, enabled, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketSourceView>, ProblemHttpResult>> GetSourceAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetBasketSourceQuery, Result<BasketSourceView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetBasketSourceQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketMembers>, ProblemHttpResult>> GetMembersAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>> handler,
        CancellationToken cancellationToken,
        string? asOf = null)
    {
        var result = await handler.HandleAsync(new GetBasketMembersQuery(id, asOf), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<AddMembersResult>, ProblemHttpResult>> AddMembersAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<AddBasketMembersCommand, AddMembersResult> handler,
        CancellationToken cancellationToken,
        [FromBody] AddMembersInput? input = null)
    {
        var result = await handler.HandleAsync(new AddBasketMembersCommand(id, input ?? new AddMembersInput(null, null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RemoveMembersResult>, ProblemHttpResult>> RemoveMembersAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult> handler,
        CancellationToken cancellationToken,
        [FromBody] RemoveMembersInput? input = null)
    {
        var result = await handler.HandleAsync(new RemoveBasketMembersCommand(id, input ?? new RemoveMembersInput(null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ReorderMembersAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<ReorderBasketMembersCommand> handler,
        CancellationToken cancellationToken,
        [FromBody] MembersOrderInput? input = null)
    {
        var result = await handler.HandleAsync(new ReorderBasketMembersCommand(id, input ?? new MembersOrderInput(null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TransferResult>, ProblemHttpResult>> TransferAsync(
        HttpContext http,
        ICommandHandler<TransferBasketMembersCommand, TransferResult> handler,
        CancellationToken cancellationToken,
        [FromBody] TransferInput? input = null)
    {
        var result = await handler.HandleAsync(new TransferBasketMembersCommand(input ?? new TransferInput(Guid.Empty, Guid.Empty, null, null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BasketHistory>, ProblemHttpResult>> GetHistoryAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetBasketHistoryQuery, Result<BasketHistory>> handler,
        CancellationToken cancellationToken,
        int? take = null)
    {
        var result = await handler.HandleAsync(new GetBasketHistoryQuery(id, take), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<InstrumentBasketView>>, ProblemHttpResult>> ListForInstrumentAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<ListInstrumentBasketsQuery, Result<IReadOnlyList<InstrumentBasketView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListInstrumentBasketsQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}

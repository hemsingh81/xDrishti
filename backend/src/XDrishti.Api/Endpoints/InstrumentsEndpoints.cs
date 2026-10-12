using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The instrument master list (NSE shares, ETFs, indices, futures; ADR 0010): search, filters, detail, and the manual refresh from Dhan's public
/// scrip master. Refreshing only reads public files, so it needs a session but no step-up. Tracking (choosing what the system works on) is the same.
/// </summary>
internal sealed class InstrumentsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/instruments").WithTags("Instruments");

        group.MapGet("/", SearchAsync)
            .WithName("SearchInstruments")
            .WithSummary("Search the master list: q (symbol, name, ISIN), type, sector, fno, asmGsm, series, tracked (true|false), includeRetired, sort, page, pageSize (max 100)")
            .ProducesValidationProblem();

        group.MapGet("/facets", GetFacetsAsync)
            .WithName("GetInstrumentFacets")
            .WithSummary("Counts of the active instruments by type, series, F&O and ASM/GSM, and of the active shares by sector (incl. Unclassified), for the filters; plus how many are tracked");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetInstrument")
            .WithSummary("One instrument with all fields and its broker ids")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Literal segments win over the {id:guid} route; the guid constraint keeps "tracked" from ever matching it.
        group.MapPost("/tracked", TrackAsync)
            .WithName("TrackInstruments")
            .WithSummary("Track 1 to 500 instruments (idempotent, all or nothing; a retired one that is not tracked yet is refused with 400 instruments.retired)")
            .ProducesValidationProblem();

        group.MapDelete("/tracked", UntrackAsync)
            .WithName("UntrackInstruments")
            .WithSummary("Untrack 1 to 500 instruments (idempotent, all or nothing; their history is kept and tracking again continues from it)")
            .ProducesValidationProblem();

        group.MapPost("/tracked/resolve", ResolveAsync)
            .WithName("ResolveInstruments")
            .WithSummary("Look up pasted tickers, canonical symbols or ISINs (up to 5,000, case-insensitive): matched instruments and the unmatched entries (never guessed)")
            .ProducesValidationProblem();

        group.MapPost("/refresh", StartRefreshAsync)
            .WithName("StartInstrumentRefresh")
            .WithSummary("Refresh the master list from Dhan in the background (force: true skips the unchanged-file check); 409 instruments.refresh_running while one runs")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/refresh/status", GetRefreshStatusAsync)
            .WithName("GetInstrumentRefreshStatus")
            .WithSummary("The refresh in progress (phase, rows read), the last finished one, the daily schedule and the number of active instruments");

        group.MapGet("/refresh/history", GetRefreshHistoryAsync)
            .WithName("ListInstrumentRefreshes")
            .WithSummary("The most recent refreshes, newest first (take 1 to 100, default 20)");
    }

    private static async Task<Results<Ok<InstrumentPage>, ProblemHttpResult>> SearchAsync(
        HttpContext http,
        IQueryHandler<SearchInstrumentsQuery, Result<InstrumentPage>> handler,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? q = null,
        string? type = null,
        string? sector = null,
        bool? fno = null,
        bool? asmGsm = null,
        string? series = null,
        bool? includeRetired = null,
        string? sort = null,
        int? page = null,
        int? pageSize = null,
        bool? tracked = null)
    {
        var request = new InstrumentSearchRequest(q, type, sector, fno, asmGsm, series, includeRetired, sort, page, pageSize, tracked);
        var result = await handler.HandleAsync(new SearchInstrumentsQuery(request), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<InstrumentFacets>> GetFacetsAsync(
        IQueryHandler<GetInstrumentFacetsQuery, InstrumentFacets> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetInstrumentFacetsQuery(), cancellationToken));

    private static async Task<Results<Ok<InstrumentDetail>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetInstrumentQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<TrackResult>, ProblemHttpResult>> TrackAsync(
        HttpContext http,
        ICommandHandler<TrackInstrumentsCommand, TrackResult> handler,
        CancellationToken cancellationToken,
        [FromBody] TrackingInput? input = null)
    {
        var result = await handler.HandleAsync(new TrackInstrumentsCommand(input ?? new TrackingInput(null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<UntrackResult>, ProblemHttpResult>> UntrackAsync(
        HttpContext http,
        ICommandHandler<UntrackInstrumentsCommand, UntrackResult> handler,
        CancellationToken cancellationToken,
        [FromBody] TrackingInput? input = null)
    {
        var result = await handler.HandleAsync(new UntrackInstrumentsCommand(input ?? new TrackingInput(null), http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ResolveResult>, ProblemHttpResult>> ResolveAsync(
        HttpContext http,
        IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> handler,
        CancellationToken cancellationToken,
        [FromBody] ResolveInput? input = null)
    {
        var result = await handler.HandleAsync(new ResolveInstrumentsQuery(input ?? new ResolveInput(null)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<RefreshStarted>, ProblemHttpResult>> StartRefreshAsync(
        HttpContext http,
        ICommandHandler<StartRefreshCommand, RefreshStarted> handler,
        CancellationToken cancellationToken,
        [FromBody] StartRefreshRequest? request = null)
    {
        var command = new StartRefreshCommand(RefreshTrigger.Manual, request?.Force ?? false, http.GetActor());
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Accepted("/api/instruments/refresh/status", result.Value);
    }

    private static async Task<Ok<RefreshStatusView>> GetRefreshStatusAsync(
        IQueryHandler<GetRefreshStatusQuery, RefreshStatusView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetRefreshStatusQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<RefreshRunView>>> GetRefreshHistoryAsync(
        IQueryHandler<ListRefreshHistoryQuery, IReadOnlyList<RefreshRunView>> handler,
        CancellationToken cancellationToken,
        int? take = null) =>
        TypedResults.Ok(await handler.HandleAsync(new ListRefreshHistoryQuery(take), cancellationToken));
}

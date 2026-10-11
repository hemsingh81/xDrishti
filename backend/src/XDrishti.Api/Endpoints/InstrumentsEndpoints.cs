using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The instrument master list (NSE shares, ETFs, indices, futures; ADR 0010): search, filters, detail, and the manual refresh from Dhan's public
/// scrip master. Refreshing only reads public files, so it needs a session but no step-up.
/// </summary>
internal sealed class InstrumentsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/instruments").WithTags("Instruments");

        group.MapGet("/", SearchAsync)
            .WithName("SearchInstruments")
            .WithSummary("Search the master list: q (symbol, name, ISIN), type, sector, fno, asmGsm, series, includeRetired, sort, page, pageSize (max 100)")
            .ProducesValidationProblem();

        group.MapGet("/facets", GetFacetsAsync)
            .WithName("GetInstrumentFacets")
            .WithSummary("Counts of the active instruments by type, series, F&O and ASM/GSM, and of the active shares by sector (incl. Unclassified), for the filters");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetInstrument")
            .WithSummary("One instrument with all fields and its broker ids")
            .ProducesProblem(StatusCodes.Status404NotFound);

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
        int? pageSize = null)
    {
        var request = new InstrumentSearchRequest(q, type, sector, fno, asmGsm, series, includeRetired, sort, page, pageSize);
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

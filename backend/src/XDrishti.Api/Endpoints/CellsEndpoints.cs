using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The cell statistics of spec P6-01 (ADR 0064): read the newest snapshot, all cells or one. Computing is the <c>xd learn cells</c> verb; nothing here writes.</summary>
internal sealed class CellsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var cells = api.MapGroup("/cells").WithTags("Learning");

        cells.MapGet("/", ListAsync)
            .WithName("ListCells")
            .WithSummary("The cell statistics of the newest snapshot: per cell and window (all, 250d, 60d) the trades, the Beta-shrunk win rate with its credible bounds, recency-weighted expectancy and profit factor, drawdown and MAE/MFE quantiles, each with its basis, knobs and limits. Filter by strategy and window; empty before the first computation. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cells.MapGet("/regimes", ListRegimesAsync)
            .WithName("ListRegimeCells")
            .WithSummary("The regime cells (P6-02): the cells of the newest snapshot that was computed with the regime split, per regime part and window, each with its trades, its Beta-shrunk win rate, the enough-evidence flag and the basis of its regime (index_and_vix or index_only; a cell never mixes the two). A regime below the minimum of trades says so. Filter by strategy and window; empty before a computation with --by-regime. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cells.MapGet("/universe", ListUniverseAsync)
            .WithName("ListUniverseCells")
            .WithSummary("The liquidity-tier cells (BK-11): the cells of the newest snapshot computed with the universe split, per tier (liq_high, liq_mid, liq_low) and window, each shrunk toward its all-shares cell, with the enough-evidence flag. Filter by strategy and window; empty before a computation with --by-universe. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cells.MapGet("/universe-links", ListUniverseLinksAsync)
            .WithName("ListUniverseLinks")
            .WithSummary("Each liquidity-tier cell read against its all-shares parent (BK-11): which cell a reader takes (child_better, no_better_than_parent, child_thin), both lower bounds, and whether the tier is an observation (at least the link minimum of trades). An observation only: it needs the tier read in place of the parent, at least the link minimum of trades and a stable hold-out (both halves of its trades beat the rest of the parent); nothing is applied and no scan scope changes. Filter by strategy; empty before a computation with --by-universe")
            .Produces<IReadOnlyList<UniverseLinkView>>();

        cells.MapGet("/{key}", GetAsync)
            .WithName("GetCell")
            .WithSummary("One cell (key strategy|tf|side|style|exit|regime, URL-encoded) in its windows from the newest snapshot that holds it. 404 learning.cell_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<CellStatsView>>, ProblemHttpResult>> ListAsync(
        string? strategy,
        string? window,
        HttpContext http,
        IQueryHandler<ListCellsQuery, Result<IReadOnlyList<CellStatsView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCellsQuery(strategy, window), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<CellStatsView>>, ProblemHttpResult>> ListRegimesAsync(
        string? strategy,
        string? window,
        HttpContext http,
        IQueryHandler<ListRegimeCellsQuery, Result<IReadOnlyList<CellStatsView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListRegimeCellsQuery(strategy, window), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<CellStatsView>>, ProblemHttpResult>> ListUniverseAsync(
        string? strategy,
        string? window,
        HttpContext http,
        IQueryHandler<ListUniverseCellsQuery, Result<IReadOnlyList<CellStatsView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListUniverseCellsQuery(strategy, window), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<UniverseLinkView>>> ListUniverseLinksAsync(
        string? strategy,
        IQueryHandler<GetUniverseLinksQuery, Result<IReadOnlyList<UniverseLinkView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetUniverseLinksQuery(strategy), cancellationToken);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<CellStatsView>>, ProblemHttpResult>> GetAsync(
        string key,
        HttpContext http,
        IQueryHandler<GetCellQuery, Result<IReadOnlyList<CellStatsView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCellQuery(key), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

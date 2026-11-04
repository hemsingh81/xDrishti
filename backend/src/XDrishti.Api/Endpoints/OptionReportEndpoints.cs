using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionReports;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The read-only P14 reports (spec P14-16): the option archive per underlying, the stored expression runs and the stored pair formations. GETs only; counts, shares, dates and verdict words, with <c>unavailable</c> and its reason in place of a non-value. They return no price, IV, Greek,
/// bar, quote or return figure.
/// </summary>
internal sealed class OptionReportEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/options").WithTags("Options")
            .MapGet("/summary", SummaryAsync)
            .WithName("GetOptionArchiveSummary")
            .WithSummary("The option archive per underlying: recorded and usable chain sessions, first and last recorded date, the gaps by outcome, the backfill keys done, empty and given up, and the half-spread model state. A figure with no source is state unavailable with its reason (not_recorded, no_model), never a number. An empty list when nothing is stored. No price, IV or Greek");

        var research = api.MapGroup("/research").WithTags("Research");
        research.MapGet("/expression-runs", RunsAsync)
            .WithName("ListExpressionRuns")
            .WithSummary("The newest stored expression runs (limit 1 to 100, default 20; profile = the 64-character lower-case hex hash): per run the profile, the counts paired and unpaired by reason, and the shares of the paired trades on a model price, a derived expiry, the recent-archive spread and each lot-feasibility word. 400 options.invalid_query for a bad limit or profile; state unavailable with no_runs and an empty list when none is stored. No price, IV or return figure")
            .ProducesValidationProblem();
        research.MapGet("/expression-runs/{id}", RunAsync)
            .WithName("GetExpressionRun")
            .WithSummary("One stored expression run by its 64-character id; 404 research.not_found for an unknown id")
            .ProducesProblem(StatusCodes.Status404NotFound);
        research.MapGet("/pair-formations", FormationsAsync)
            .WithName("ListPairFormations")
            .WithSummary("The stored pair formations whose window overlaps from to to (default the last 400 days; at most 400), oldest quarter first: candidates, tested, unavailable, selected, the Benjamini-Hochberg threshold and the half-life distribution in sessions. 400 options.invalid_query for an inverted or over-long range; state unavailable with no_runs and an empty list when none is stored")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<OptionArchiveSummaryView>, ProblemHttpResult>> SummaryAsync(HttpContext http, IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetOptionArchiveSummaryQuery(), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ExpressionRunList>, ProblemHttpResult>> RunsAsync(HttpContext http, IQueryHandler<ListExpressionRunsQuery, Result<ExpressionRunList>> handler, CancellationToken cancellationToken, string? profile = null, int? limit = null)
    {
        var result = await handler.HandleAsync(new ListExpressionRunsQuery(profile, limit), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ExpressionRunSummary>, ProblemHttpResult>> RunAsync(HttpContext http, string id, IQueryHandler<GetExpressionRunQuery, Result<ExpressionRunSummary>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetExpressionRunQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PairFormationList>, ProblemHttpResult>> FormationsAsync(HttpContext http, IQueryHandler<ListPairFormationsQuery, Result<PairFormationList>> handler, CancellationToken cancellationToken, DateOnly? from = null, DateOnly? to = null)
    {
        var result = await handler.HandleAsync(new ListPairFormationsQuery(from, to), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}

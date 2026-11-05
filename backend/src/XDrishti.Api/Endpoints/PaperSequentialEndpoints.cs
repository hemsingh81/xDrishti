using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The sequential failure test of the paper edge series (spec CF-09, ADR 0213). One GET, computed on read; nothing is stored, nothing is changed, no order exists.</summary>
internal sealed class PaperSequentialEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/paper").WithTags("Paper")
            .MapGet("/sequential", StatusAsync)
            .WithName("GetPaperSequentialStatus")
            .WithSummary("The sequential failure test of the paper edge series (graded shadow trades that count for edge, current paper epoch, in session then trade-key order) with the P10 behavioural series beside it: state (monitoring, on_track, caution or failure; failure is latched for the epoch), running sum, boundaries and odds, trades counted, effective n, first crossing, the correlation used, the settings hash and a path summary. scope=system (default) or scope=strategy with strategy=<id>. A flag only: report only, never a pass; the fixed P10 gates still decide a pass. Gaussian model, modelled fills. Read only; no order exists. 404 paper.not_found when no shadow edge candidate exists in the scope, 400 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<SequentialStatusView>, ProblemHttpResult>> StatusAsync(
        string? scope,
        string? strategy,
        HttpContext http,
        IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSequentialStatusQuery(scope ?? GetSequentialStatusQuery.SystemScope, strategy), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        return result.Value.HasSeries
            ? TypedResults.Ok(result.Value)
            : ProblemResults.From(Error.NotFound("paper.not_found", "No shadow edge candidate of the current paper epoch exists in this scope: there is no series to test."), http);
    }
}

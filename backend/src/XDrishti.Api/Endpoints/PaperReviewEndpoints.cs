using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The weekly paper review and the discipline report of spec P10-03 (ADR 0121). Read only: a review is stored by the paper-review service or <c>xd paper review --store</c>; nothing here stores, applies or places anything.</summary>
internal sealed class PaperReviewEndpoints : IEndpointModule
{
    private const string Paper = "paper";

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/paper").WithTags("Paper");

        group.MapGet("/reviews", ListAsync)
            .WithName("ListPaperReviews")
            .WithSummary("The stored weekly paper reviews, newest week first (optional epoch; limit 1 to 52, default 12). Read only. 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/reviews/{isoWeek}", GetAsync)
            .WithName("GetPaperReview")
            .WithSummary("The stored weekly paper review of an ISO week (2026-W43) and epoch (default the newest): the text exactly as it was built, and its sections. Opens with 'Paper only: no order was placed and none exists.' Read only. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/review", DryRunAsync)
            .WithName("GetPaperReviewDryRun")
            .WithSummary("A weekly paper review built now from stored records and not stored (week=2026-W43, default this week; optional epoch). An older week is the state as of today, and says so. Read only. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/discipline", DisciplineAsync)
            .WithName("GetPaperDiscipline")
            .WithSummary("The discipline report of a window (epoch, from, to; default the whole epoch up to the last closed session): the seven categories of the primary tickets (they add up to the primary count), what skipping and resizing cost in R with n and state, added real fills, and the paper execution gap. No composite score. Read only. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<PaperReviewList>, ProblemHttpResult>> ListAsync(long? epoch, int? limit, HttpContext http, IQueryHandler<ListPaperReviewsQuery, Result<PaperReviewList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListPaperReviewsQuery(epoch, limit ?? 12), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PaperReviewView>, ProblemHttpResult>> GetAsync(string isoWeek, long? epoch, HttpContext http, IQueryHandler<GetPaperReviewQuery, Result<PaperReviewView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPaperReviewQuery(isoWeek, epoch), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PaperReviewView>, ProblemHttpResult>> DryRunAsync(string? week, long? epoch, HttpContext http, ICommandHandler<BuildPaperReviewCommand, PaperReviewView> handler, CancellationToken cancellationToken)
    {
        // Store is always false here: the endpoint cannot store.
        var result = await handler.HandleAsync(new BuildPaperReviewCommand(week, epoch, false, "api:" + Paper), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DisciplineView>, ProblemHttpResult>> DisciplineAsync(long? epoch, DateOnly? from, DateOnly? to, HttpContext http, IQueryHandler<GetDisciplineQuery, Result<DisciplineView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDisciplineQuery(epoch, from, to), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the other paper endpoints; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

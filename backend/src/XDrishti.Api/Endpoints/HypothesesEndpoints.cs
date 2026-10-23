using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The hypothesis log of spec P6-10 (ADR 0076): read the hypotheses and one with its history. Writing is the <c>xd learn hypothesis</c> verb; nothing here changes a hypothesis, runs a test or applies a change.</summary>
internal sealed class HypothesesEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/hypotheses").WithTags("Learning");

        group.MapGet("/", ListAsync)
            .WithName("ListHypotheses")
            .WithSummary("The hypotheses (one knob each, from the owner or the grid), newest first, each in its current state (proposed, approved, running, judged, accepted, rejected, expired); optional state filter, limit 1 to 200 (default 50). Empty before the first. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetHypothesis")
            .WithSummary("One hypothesis with its history, the states it may move to and the experiment that judged it. Accepting records a verdict only; applying a change is the owner's own act. 404 hypotheses.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<HypothesisView>>, ProblemHttpResult>> ListAsync(
        string? state,
        int? limit,
        HttpContext http,
        IQueryHandler<ListHypothesesQuery, Result<IReadOnlyList<HypothesisView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListHypothesesQuery(state, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<HypothesisView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetHypothesisQuery, Result<HypothesisView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetHypothesisQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

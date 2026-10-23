using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The experiment ledger of spec P6-11 (ADR 0072): read the recorded knob changes and one in full. Judging a change is the <c>xd learn experiment</c> verb; nothing here writes.</summary>
internal sealed class ExperimentsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var experiments = api.MapGroup("/experiments").WithTags("Learning");

        experiments.MapGet("/", ListAsync)
            .WithName("ListExperiments")
            .WithSummary("The recorded experiments (knob changes judged against a baseline run), newest first (limit 1 to 200, default 50): the knob, the outcome (Better, NotBetter, Inconclusive or CheaperAssumption), folds better of folds used, the improvement required after the trials, totals after the expected live gap, and the measured time of the comparison. Empty before the first experiment. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        experiments.MapGet("/{id:long}", GetAsync)
            .WithName("GetExperiment")
            .WithSummary("One experiment with its full report, honest limits and the rules it was judged with. 404 experiments.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<ExperimentView>>, ProblemHttpResult>> ListAsync(
        int? limit,
        HttpContext http,
        IQueryHandler<ListExperimentsQuery, Result<IReadOnlyList<ExperimentView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListExperimentsQuery(limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ExperimentView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetExperimentQuery, Result<ExperimentView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetExperimentQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

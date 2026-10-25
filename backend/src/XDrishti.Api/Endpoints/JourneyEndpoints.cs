using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The Journey of spec P8-12 (ADR 0110): the stored frozen-model counterfactual and the plan-price weeks, read only. A counterfactual is computed and stored by <c>xd learn counterfactual --store</c>; nothing here fits a model, stores, applies or approves anything.</summary>
internal sealed class JourneyEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var learning = api.MapGroup("/learning").WithTags("Learning");

        learning.MapGet("/counterfactual", GetAsync)
            .WithName("GetLearningCounterfactual")
            .WithSummary("A stored frozen-model counterfactual (id=), or the newest: outcome, period, knobs, windows (both sides, paired gain, take-R uplift), the judged and pooled blocks, reasons, limits and basis. Read only. 404 learning.counterfactual_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        learning.MapGet("/counterfactuals", ListAsync)
            .WithName("ListLearningCounterfactuals")
            .WithSummary("The stored counterfactuals, newest first (limit 1 to 50, default 12). Read only. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        learning.MapGet("/journey", JourneyAsync)
            .WithName("GetLearningJourney")
            .WithSummary("The Journey: header of the newest stored counterfactual, plan-price grades by ISO week (simulated on plan prices), the live line (always not_evaluable until real trades exist) and the evidence block. Read only");
    }

    private static async Task<Results<Ok<CounterfactualReportView>, ProblemHttpResult>> GetAsync(
        long? id,
        HttpContext http,
        IQueryHandler<GetCounterfactualQuery, Result<CounterfactualReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCounterfactualQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<CounterfactualHeaderView>>, ProblemHttpResult>> ListAsync(
        int? limit,
        HttpContext http,
        IQueryHandler<ListCounterfactualsQuery, Result<IReadOnlyList<CounterfactualHeaderView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCounterfactualsQuery(limit ?? 12), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<JourneyView>, ProblemHttpResult>> JourneyAsync(
        HttpContext http,
        IQueryHandler<GetJourneyQuery, Result<JourneyView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetJourneyQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}

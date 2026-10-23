using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The plan record of spec P7-03 (ADR 0083): read the plans and one with its tickets and trail. Read only; no order exists anywhere in the system, and nothing here changes a plan.</summary>
internal sealed class PlansEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/plans").WithTags("Plans");

        group.MapGet("/", ListAsync)
            .WithName("ListPlans")
            .WithSummary("The plans, newest session first, each in its current state; optional session date and account filters, limit 1 to 200 (default 50). Empty before the first. No order is placed by any plan. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetPlan")
            .WithSummary("One plan with its tickets (primary, reserve, rejected with reasons and evidence), size per account, the states they may move to and the trail of every state change. Armed means watched, never an order. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<PlanView>>, ProblemHttpResult>> ListAsync(
        DateOnly? date,
        Guid? account,
        int? limit,
        HttpContext http,
        IQueryHandler<ListPlansQuery, Result<IReadOnlyList<PlanView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListPlansQuery(date, account, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PlanView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetPlanQuery, Result<PlanView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PlanErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
